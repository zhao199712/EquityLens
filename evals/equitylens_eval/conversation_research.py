"""Bounded claim checks for the journey corpus, not a general semantic judge.

Do not infer correctness from a number elsewhere in an answer or from OutputGate.
Unknown syntax stays unknown; known wrong/missing claims dominate unknown checks.
"""
from __future__ import annotations

import json
import hashlib
import re
from pathlib import Path

from . import normalize as n
from .schema import EvalCase
from .scorers import _fact_matches

RUBRIC_PATH = Path(__file__).resolve().parent.parent / 'cases/conversation_research_rubric_v2.json'


def oracle_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                     allow_nan=False, separators=(',', ':')).encode()).hexdigest()


def validate_rubric(rubric):
    if rubric.get('version') != 2 or not rubric.get('companies') or not rubric.get('cases'):
        raise ValueError('Unsupported or incomplete conversation scoring rubric')
    for rule in rubric['cases'].values():
        if not re.fullmatch(r'[0-9a-f]{64}', rule.get('oracle_hash', '')):
            raise ValueError('Rubric must bind the reviewed research oracle hash')
        if not rule.get('metrics') or not rule.get('source', {}).get('pdf_sha256'):
            raise ValueError('Rubric lacks reviewed source evidence')
        for metric in rule['metrics']:
            if not metric.get('pages') or not metric.get('source_terms') or not metric.get('aliases'):
                raise ValueError('Rubric metric lacks source or claim rules')
            for pattern in [*metric['aliases'], *metric.get('source_patterns', [])]:
                re.compile(pattern)
    return rubric


def load_rubric(path=None):
    return validate_rubric(json.loads((path or RUBRIC_PATH).read_text()))


def aggregate(values):
    values = list(values)
    if False in values:
        return False
    return True if values and all(v is True for v in values) else None


def item(name, value, reason, **details):
    return dict(check=name, status='unverifiable' if value is None else 'pass' if value else 'fail',
                reason=reason, **details)


def state(part):
    return {'pass': True, 'fail': False, 'unverifiable': None}[part['status']]


def clean(text):
    return n.normalize_text(text).replace('**', '').replace('__', '').replace('−', '-')


def years(text):
    return [(m.start(), int(m[1]) if len(m[1]) == 4 else int(m[1])+1911)
            for m in re.finditer(r'(?<!\d)(20\d{2}|1[01]\d)\s*年', text)]


def companies(text, rubric):
    found = []
    for ticker, aliases in rubric['companies'].items():
        for alias in [ticker, *aliases]:
            found.extend((m.start(), ticker) for m in re.finditer(re.escape(alias), text, re.I))
    # Explicit unknown tickers must not inherit the requested company.
    found.extend((m.start(), m[1]) for m in re.finditer(r'(?<!\d)([1-9]\d{3})(?!\d|\s*年)', text)
                 if not 1900 <= int(m[1]) <= 2100)
    return sorted(set(found))


def scope_before(found, position, default):
    before = [v for pos, v in found if pos <= position]
    return before[-1] if before else default


def units(answer):
    """Paragraphs and Markdown table cells with their own column header.

Headings supply entity/year context. A citation-only suffix belongs to the
preceding sentence; citations in another claim never establish support.
"""
    heading = ''
    headers = None
    for line in clean(answer).splitlines():
        line = line.strip()
        if not line:
            continue
        if line.startswith('#'):
            heading = line
            continue
        if line.startswith('|') and line.endswith('|'):
            cells = [c.strip() for c in line.strip('|').split('|')]
            if all(re.fullmatch(r'[:\- ]+', c) for c in cells):
                continue
            if headers is None:
                headers = cells
                continue
            for index, cell in enumerate(cells[1:], 1):
                if index < len(headers):
                    yield heading, cells[0]+' '+headers[index]+' '+cell, True
            continue
        headers = None
        yield heading, line, False


def claim_mentions(answer, rule, metric, rubric):
    """Recognize explicit prose labels and tables; ambiguous layouts need review."""
    claims = []
    all_metrics = [m for r in rubric['cases'].values() for m in r['metrics']]
    introduction = ''
    for heading, text, table in units(answer):
        if not heading and not re.search(r'\d\s*(?:%|元)', text) and (companies(text, rubric) or years(text)):
            introduction = text
        heading = heading or introduction
        heading_companies = companies(heading, rubric)
        heading_years = years(heading)
        default_company = heading_companies[-1][1] if heading_companies else rule['ticker']
        default_year = heading_years[-1][1] if heading_years else rule['year']
        if not any(re.search(a, text, re.I) for a in metric['aliases']):
            continue
        if metric.get('subject') and not any(s in text for s in metric['subject']):
            continue
        # Label the numeric span using the nearest preceding metric, including
        # diluted EPS, so a basic EPS heading cannot absorb diluted EPS numbers.
        anchors = []
        for candidate in all_metrics:
            for alias in candidate['aliases']:
                anchors.extend((m.start(), m.end(), candidate['id']) for m in re.finditer(alias, text, re.I))
        anchors.extend((m.start(), m.end(), 'diluted_eps') for m in re.finditer(r'稀釋\s*(?:EPS|每股盈餘)', text, re.I))
        anchors.extend((m.start(), m.end(), 'other_metric') for m in re.finditer(r'別的指標|其他指標', text))
        for match in re.finditer(r'[-+]?\d[\d,]*(?:\.\d+)?\s*(?:%|元|美元)', text):
            previous = [a for a in anchors if a[1] <= match.start()]
            if not previous:
                continue
            anchor = max(previous, key=lambda a: (a[1], -a[0]))
            if anchor[2] != metric['id']:
                continue
            mention = n.extract_numbers(match[0])[0]
            kind, _ = n.fact_kind_and_scale(metric['unit'])
            if mention.kind != kind:
                continue
            before = text[:match.start()]
            if re.search(r'(?:不是|並非|而非)\s*$', before):
                continue
            company = scope_before(companies(text, rubric), match.start(), default_company)
            year_marks = years(text) + [(m.start(), rule['year']-1) for m in re.finditer(r'前一年|前年度|去年', text)]
            year = scope_before(sorted(year_marks), match.start(), default_year)
            tail_scope = re.match(r'\s*[(（]([^()（）]+)[)）]', text[match.end():])
            if tail_scope:
                tail_companies, tail_years = companies(tail_scope[1], rubric), years(tail_scope[1])
                company = tail_companies[-1][1] if tail_companies else company
                year = tail_years[-1][1] if tail_years else year
            quarter = bool(re.search(r'(?:第[一二三四1234]季|[Qq][1-4]|單季)', before))
            reported_integer = (metric.get('reported_integer') is not None and mention.value == metric['reported_integer']
                                and re.search(r'(?:表列|整數|約數)[^。；;\n]{0,24}$', before)
                                and not re.search(r'[÷≈=][^。；;\n]{0,12}$', before))
            next_anchor = min((a[0] for a in anchors if a[0] > match.end() and a[2] != metric['id']), default=len(text))
            local_refs = n.citation_indices(text[anchor[0]:next_anchor])
            if not local_refs and re.search(r'(?:\[\d+\])+[。.]?$', text):
                first_ref = re.search(r'\[\d+\]', text)
                # A shared citation suffix may support multiple metrics in one paragraph.
                if first_ref and not re.search(r'\d+(?:\.\d+)?\s*(?:%|元)', text[first_ref.start():]):
                    local_refs = n.citation_indices(text[first_ref.start():])
            if not local_refs and re.match(r'^\s*(?:\[\d+\])+', text):
                local_refs = n.citation_indices(text[:anchor[0]])
            claims.append(dict(text=text, raw=mention.raw, value=mention.value, kind=mention.kind,
                               company=company, year=year, quarter=quarter,
                               foreign_currency='美元' in mention.raw or bool(re.search(r'(?:美元|美金|USD|US\$)\s*$', before)),
                               reported_integer=bool(reported_integer),
                               citations=local_refs, table=table))
    return claims


def source_support(claim, citations, rule, metric):
    refs = claim['citations']
    if not refs:
        return False, 'Claim has no citation in its paragraph/metric span/cell'
    outcomes = []
    for index in refs:
        citation = next((c for c in citations if isinstance(c, dict) and c.get('citationIndex') == index), None)
        if citation is None:
            outcomes.append(False)
            continue
        metadata = (citation.get('documentType') == rule['source']['document_type']
                    and citation.get('title') == rule['source']['title']
                    and citation.get('pageNumber') in metric['pages']
                    and (not citation.get('documentId') or citation['documentId'] == rule['source']['document_id']))
        if not metadata:
            outcomes.append(False)
            continue
        quote = citation.get('quoteText')
        if not isinstance(quote, str) or not quote.strip():
            outcomes.append(None)
            continue
        compact = re.sub(r'[\s,*_$|]', '', clean(quote))
        outcomes.append(all(any(re.sub(r'[\s,*_$|]', '', term) in compact for term in group)
                            for group in metric['source_terms'])
                        and all(re.search(pattern, clean(quote)) for pattern in metric.get('source_patterns', [])))
    if True in outcomes:
        return True, 'Claim cites a reviewed page and quote contains the supporting metric/operands'
    if None in outcomes:
        return None, 'Eligible citation exists but supporting quote was not captured'
    return False, 'Claim citations do not support this metric on a reviewed source page'


def evaluate(row, refs, research, rubric=None):
    rubric = rubric or load_rubric()
    captured = row.get('research')
    if not isinstance(captured, dict) and isinstance(row.get('answer'), str):
        captured = {'answer': row['answer']}
    if not isinstance(captured, dict) or not isinstance(captured.get('answer'), str):
        parts = [item('research_capture', None, 'Research answer artifact was not captured')]
        return None, parts
    answer = captured['answer']
    citations = captured.get('citations')
    parts = []
    if not refs:
        return None, [item('research_oracle', None, 'No required research references')]
    for ref in refs:
        rule = rubric['cases'].get(ref)
        if not rule or ref not in research:
            parts.append(item('research_oracle', None, 'No scoped rule for this reference', reference=ref))
            continue
        if oracle_hash(research[ref]) != rule['oracle_hash']:
            parts.append(item('research_oracle', None, 'Saved oracle differs from the reviewed rubric', reference=ref))
            continue
        case = EvalCase.from_dict(research[ref])
        for fact in case.facts:
            metric = next((m for m in rule['metrics'] if m['label'] == fact.label), None)
            if metric is None or not fact.verified:
                parts.append(item('research_fact', None, 'No scoped verified metric rule', reference=ref, metric=fact.label))
                continue
            claims = claim_mentions(answer, rule, metric, rubric)
            relevant = [c for c in claims if c['company'] == rule['ticker'] and c['year'] == rule['year'] and not c['quarter']]
            kind, scale = n.fact_kind_and_scale(fact.unit)
            tolerance, tolerance_type = n.default_tolerance(kind)
            tolerance = fact.tolerance if fact.tolerance is not None else tolerance
            tolerance_type = fact.tolerance_type or tolerance_type
            if tolerance_type == 'abs' and kind == 'money':
                tolerance *= scale
            matches = [c for c in relevant if not c['foreign_currency'] and (c['reported_integer'] or _fact_matches(
                n.NumberMention(c['value'], c['kind'], c['raw']), fact.value*scale,
                kind, scale, tolerance, tolerance_type))]
            wrong = [c for c in relevant if c not in matches]
            if wrong:
                value, reason = False, 'Wrong or contradictory value for the requested entity, year and metric'
            elif matches:
                value, reason = True, 'Scoped metric value matches the verified oracle'
            elif claims:
                value, reason = False, 'Only another entity, year or quarter has the required metric'
            elif metric['id'] == 'basic_eps' and re.search(r'稀釋\s*(?:EPS|每股盈餘)', clean(answer), re.I):
                value, reason = False, 'Diluted EPS cannot satisfy the required basic EPS fact'
            elif any(re.search(alias+r'[^。\n]{0,24}(?:未提供|無法|不足|未知)', clean(answer), re.I) for alias in metric['aliases']):
                value, reason = False, 'Completed answer explicitly omits the required fact'
            elif any(_fact_matches(m, fact.value*scale, kind, scale, tolerance, tolerance_type)
                     for m in n.extract_numbers(clean(answer))):
                value, reason = None, 'Expected number occurs but its metric binding is ambiguous'
            else:
                value, reason = False, 'Completed answer omits the required fact or explicitly cannot answer it'
            parts.append(item('research_fact', value, reason, reference=ref, metric=fact.label,
                              claims=claims, expected=dict(value=fact.value, unit=fact.unit)))
            if not isinstance(citations, list):
                support, reason = None, 'Citation artifact was not captured'
            elif not relevant:
                support, reason = None, 'No correctly scoped claim to bind to a source'
            else:
                results = [source_support(c, citations, rule, metric) for c in relevant]
                support = aggregate(v for v, _ in results)
                reason = '; '.join(sorted({why for _, why in results}))
            parts.append(item('research_source_support', support, reason, reference=ref, metric=fact.label))
    if isinstance(citations, list):
        indices = [c.get('citationIndex') for c in citations if isinstance(c, dict)]
        refs_in_answer = n.citation_indices(clean(answer))
        valid = len(indices) == len(citations) and len(indices) == len(set(indices)) and all(i in indices for i in refs_in_answer)
        # An empty completed answer/refusal is already a completeness failure;
        # missing citation markers do not make that failure unknown.
        parts.append(item('research_citation_integrity', valid if refs_in_answer else None,
                          'Checks reference identity only, not semantic entailment'))
    else:
        parts.append(item('research_citation_integrity', None, 'Citation artifact was not captured'))
    return aggregate(state(p) for p in parts), parts
