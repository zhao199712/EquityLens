namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class ContextFormatter : IContextFormatter
{
    public string Format(IReadOnlyList<SelectedChunk> chunks)
    {
        var context = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var selected = chunks[i];
            var result = selected.Chunk.Result;
            var pageInfo = result.PageNumber.HasValue ? $" (Page {result.PageNumber})" : "";
            var documentType = string.IsNullOrWhiteSpace(result.DocumentType) ? "Unknown" : result.DocumentType;
            context.Add($"[{selected.Index}] {result.DocumentTitle}{pageInfo} | DocumentType: {documentType} | SourceRole: {selected.Chunk.SourceRole}\n{result.Content}");
        }

        return string.Join("\n\n---\n\n", context);
    }
}
