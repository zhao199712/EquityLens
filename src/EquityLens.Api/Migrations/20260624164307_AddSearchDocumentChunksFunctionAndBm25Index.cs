using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    public partial class AddSearchDocumentChunksFunctionAndBm25Index : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.search_document_chunks(
                    query_text text DEFAULT NULL::text,
                    query_embedding vector DEFAULT NULL::vector,
                    ticker_filter text DEFAULT NULL::text,
                    document_type_filter text DEFAULT NULL::text,
                    top_k integer DEFAULT 10,
                    mode text DEFAULT 'vector'::text
                )
                RETURNS TABLE(
                    document_chunk_id uuid, document_id uuid, document_title text,
                    document_type text, source_url text, chunk_index int, page_number int,
                    section_title text, content text, score double precision,
                    security_id uuid, ticker text, exchange text, security_name text, search_mode text
                )
                LANGUAGE plpgsql VOLATILE
                AS $function$
                DECLARE
                    bm25_top_k int;
                    vector_top_k int;
                BEGIN
                    IF mode = 'vector' THEN
                        RETURN QUERY SELECT dc.id, d.id, d.title::text, d.document_type::text,
                            coalesce(ic.source_url, ff.source_url, d.source_url)::text,
                            dc.chunk_index, dc.page_number, dc.section_title::text, dc.content,
                            (1 - (de.embedding <=> query_embedding))::double precision,
                            s.id, s.ticker::text, s.exchange::text, s.name::text, 'vector'::text
                        FROM document_embedding de
                        INNER JOIN document_chunk dc ON dc.id = de.document_chunk_id
                        INNER JOIN document d ON d.id = dc.document_id
                        LEFT JOIN investor_conference ic ON ic.document_id = d.id
                        LEFT JOIN financial_filing ff ON ff.document_id = d.id
                        LEFT JOIN security s ON s.id = coalesce(ic.security_id, ff.security_id)
                        WHERE (ticker_filter IS NULL OR s.ticker = ticker_filter OR s.ticker = upper(ticker_filter))
                          AND (document_type_filter IS NULL OR d.document_type = document_type_filter)
                        ORDER BY de.embedding <=> query_embedding LIMIT top_k;

                    ELSIF mode = 'bm25' THEN
                        IF query_text IS NULL THEN RAISE EXCEPTION 'query_text required for bm25'; END IF;
                        RETURN QUERY SELECT dc.id, d.id, d.title::text, d.document_type::text,
                            coalesce(ic.source_url, ff.source_url, d.source_url)::text,
                            dc.chunk_index, dc.page_number, dc.section_title::text, dc.content,
                            coalesce(pdb.score(dc.id), 0)::double precision,
                            s.id, s.ticker::text, s.exchange::text, s.name::text, 'bm25'::text
                        FROM document_chunk dc
                        INNER JOIN document d ON d.id = dc.document_id
                        LEFT JOIN investor_conference ic ON ic.document_id = d.id
                        LEFT JOIN financial_filing ff ON ff.document_id = d.id
                        LEFT JOIN security s ON s.id = coalesce(ic.security_id, ff.security_id)
                        WHERE (ticker_filter IS NULL OR s.ticker = ticker_filter OR s.ticker = upper(ticker_filter))
                          AND (document_type_filter IS NULL OR d.document_type = document_type_filter)
                          AND dc.content ||| query_text
                        ORDER BY pdb.score(dc.id) DESC LIMIT top_k;

                    ELSIF mode = 'hybrid' THEN
                        IF query_text IS NULL OR query_embedding IS NULL THEN
                            RAISE EXCEPTION 'query_text and query_embedding required for hybrid'; END IF;
                        bm25_top_k := top_k * 3; vector_top_k := top_k * 3;

                        CREATE TEMP TABLE IF NOT EXISTS _vr2(cid uuid, v double precision) ON COMMIT DROP;
                        CREATE TEMP TABLE IF NOT EXISTS _br2(cid uuid, b double precision) ON COMMIT DROP;
                        DELETE FROM _vr2; DELETE FROM _br2;

                        INSERT INTO _vr2
                        SELECT dc.id, (1 - (de.embedding <=> query_embedding))::double precision
                        FROM document_embedding de INNER JOIN document_chunk dc ON dc.id = de.document_chunk_id
                        INNER JOIN document d ON d.id = dc.document_id
                        LEFT JOIN financial_filing ff ON ff.document_id = d.id
                        LEFT JOIN investor_conference ic ON ic.document_id = d.id
                        LEFT JOIN security s ON s.id = coalesce(ic.security_id, ff.security_id)
                        WHERE (ticker_filter IS NULL OR s.ticker = ticker_filter OR s.ticker = upper(ticker_filter))
                          AND (document_type_filter IS NULL OR d.document_type = document_type_filter)
                        ORDER BY de.embedding <=> query_embedding LIMIT vector_top_k;

                        INSERT INTO _br2
                        SELECT dc.id, coalesce(pdb.score(dc.id), 0)::double precision
                        FROM document_chunk dc INNER JOIN document d ON d.id = dc.document_id
                        LEFT JOIN financial_filing ff ON ff.document_id = d.id
                        LEFT JOIN investor_conference ic ON ic.document_id = d.id
                        LEFT JOIN security s ON s.id = coalesce(ic.security_id, ff.security_id)
                        WHERE (ticker_filter IS NULL OR s.ticker = ticker_filter OR s.ticker = upper(ticker_filter))
                          AND (document_type_filter IS NULL OR d.document_type = document_type_filter)
                          AND dc.content ||| query_text
                        ORDER BY pdb.score(dc.id) DESC LIMIT bm25_top_k;

                        RETURN QUERY
                        WITH ranked AS (
                            SELECT cid, v, NULL::double precision AS bscore,
                                   ROW_NUMBER() OVER (ORDER BY v DESC) AS r FROM _vr2
                            UNION ALL
                            SELECT cid, NULL, b,
                                   ROW_NUMBER() OVER (ORDER BY b DESC) FROM _br2
                        ),
                        deduped AS (
                            SELECT cid, MAX(v) AS vmax, MAX(bscore) AS bmax FROM ranked GROUP BY cid
                        ),
                    combined AS (
                        SELECT d.cid,
                               (coalesce(1.0/(60.0+r_v.r),0.0) + coalesce(1.0/(60.0+r_b.r),0.0))::double precision AS hscore,
                               coalesce(r_v.v,0.0)::double precision AS bv, coalesce(r_b.b,0.0)::double precision AS bb
                        FROM deduped d
                            LEFT JOIN (SELECT cid, v, ROW_NUMBER() OVER (ORDER BY v DESC) AS r FROM _vr2) r_v USING (cid)
                            LEFT JOIN (SELECT cid, b, ROW_NUMBER() OVER (ORDER BY b DESC) AS r FROM _br2) r_b USING (cid)
                        )
                        SELECT dc.id, d.id, d.title::text, d.document_type::text,
                            coalesce(ic.source_url, ff.source_url, d.source_url)::text,
                            dc.chunk_index, dc.page_number, dc.section_title::text, dc.content,
                            c.hscore, s.id, s.ticker::text, s.exchange::text, s.name::text,
                            CASE WHEN c.bb > 0 AND c.bv > 0 THEN 'hybrid' WHEN c.bb > 0 THEN 'bm25' ELSE 'vector' END
                        FROM combined c
                        INNER JOIN document_chunk dc ON dc.id = c.cid
                        INNER JOIN document d ON d.id = dc.document_id
                        LEFT JOIN investor_conference ic ON ic.document_id = d.id
                        LEFT JOIN financial_filing ff ON ff.document_id = d.id
                        LEFT JOIN security s ON s.id = coalesce(ic.security_id, ff.security_id)
                        ORDER BY c.hscore DESC LIMIT top_k;
                    ELSE
                        RAISE EXCEPTION 'unknown mode: %', mode;
                    END IF;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS document_chunk_search_idx
                ON public.document_chunk
                USING bm25 (
                    id,
                    ((content)::pdb.jieba),
                    ((section_title)::pdb.jieba)
                )
                WITH (key_field='id');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS document_chunk_search_idx;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.search_document_chunks;");
        }
    }
}
