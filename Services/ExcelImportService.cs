using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using TransportBidPortal.Contracts;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Services;

public interface IExcelImportService
{
    List<BidLane> ParseLanes(IFormFile file);
    List<BidLane> ParseLanes(IFormFile file, List<TemplateFieldDefinition> templateFields);
    ExcelAnalysisResult AnalyzeColumns(IFormFile file);
    ExcelAnalysisResult AnalyzeColumns(string filePath);
    List<TemplateHeaderMatch> MatchTemplate(IFormFile file, List<TemplateFieldDefinition> templateFields);
    byte[] BuildTemplateWorkbook(List<TemplateFieldDefinition> templateFields);
    ExtractHeadersResult ExtractHeaders(IFormFile file);
    TransformPreviewResult TransformExcel(IFormFile file, int headerRow, List<ColumnMappingInput> mappings, int maxRows = 0);
    List<ExcelColumnMatch> AutoMapHeaders(List<SourceHeader> sourceHeaders, List<TemplateFieldDefinition> templateFields);
    ExcelRawPreviewResult PreviewRaw(IFormFile file, int maxRows = 30);
}

public class ExcelImportService : IExcelImportService
{
    private sealed record HeaderCandidate(string Raw, int Index, string Normalized);

    private static readonly Dictionary<string, string[]> CanonicalAliases = new()
    {
        ["Origin"] = ["origin", "origem", "cidade origem", "origem cidade", "from", "source", "origem uf", "origem cidade uf"],
        ["Destination"] = ["destination", "destino", "cidade destino", "destino cidade", "to", "target", "destino uf", "destino cidade uf"],
        ["Route"] = ["route", "rota", "lane", "trecho", "origem destino", "rota origem destino"],
        ["FreightType"] = ["freight type", "freighttype", "tipo frete", "tipofrete", "tipo de frete", "modalidade"],
        ["VolumeForecast"] = ["volume forecast", "volumeforecast", "volume", "previsao volume", "forecast"],
        ["SlaRequirements"] = ["sla", "sla requirements", "slarequirements", "nivel de servico", "service level"],
        ["VehicleType"] = ["vehicle type", "vehicletype", "tipo veiculo", "tipo de veiculo", "veiculo", "truck type"],
        ["InsuranceRequirements"] = ["insurance requirements", "insurancerequirements", "seguro", "requisito seguro"],
        ["PaymentTerms"] = ["payment terms", "payment", "prazo pagamento", "condicao pagamento", "payment condition"],
        ["Region"] = ["region", "regiao", "macro regiao", "cluster", "zona"]
    };

    public List<BidLane> ParseLanes(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var analysis = AnalyzeWorkbook(sheet);
        var mapping = analysis.Matches.ToDictionary(x => x.CanonicalField, x => x.ColumnIndex, StringComparer.OrdinalIgnoreCase);
        var headerRow = DetectHeaderRow(sheet);

        string GetCell(IXLRow row, string canonical)
        {
            return mapping.TryGetValue(canonical, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;
        }

        decimal GetDec(IXLRow row, string canonical)
        {
            var value = GetCell(row, canonical);
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv))
            {
                return inv;
            }

            if (decimal.TryParse(value, NumberStyles.Any, new CultureInfo("pt-BR"), out var pt))
            {
                return pt;
            }

            return 0m;
        }

        var lanes = new List<BidLane>();
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow))
        {
            var origin = GetCell(row, "Origin");
            var destination = GetCell(row, "Destination");
            var route = GetCell(row, "Route");

            if ((string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination)) &&
                !string.IsNullOrWhiteSpace(route))
            {
                var split = SplitRoute(route);
                origin = split.Origin;
                destination = split.Destination;
            }

            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            lanes.Add(new BidLane
            {
                Origin = origin,
                Destination = destination,
                FreightType = GetCell(row, "FreightType"),
                VolumeForecast = GetDec(row, "VolumeForecast"),
                SlaRequirements = GetCell(row, "SlaRequirements"),
                VehicleType = GetCell(row, "VehicleType"),
                InsuranceRequirements = GetCell(row, "InsuranceRequirements"),
                PaymentTerms = GetCell(row, "PaymentTerms"),
                Region = GetCell(row, "Region")
            });
        }

        return lanes;
    }

    public List<BidLane> ParseLanes(IFormFile file, List<TemplateFieldDefinition> templateFields)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var headerRow = DetectHeaderRow(sheet);
        var matches = MatchTemplate(file, templateFields);
        var mapping = matches
            .Where(x => !string.IsNullOrWhiteSpace(x.MatchedHeader))
            .ToDictionary(
                x => x.TemplateKey,
                x =>
                {
                    var headerCell = sheet.Row(headerRow).CellsUsed()
                        .FirstOrDefault(c => string.Equals(c.GetString().Trim(), x.MatchedHeader, StringComparison.OrdinalIgnoreCase));
                    return headerCell?.Address.ColumnNumber ?? -1;
                },
                StringComparer.OrdinalIgnoreCase);

        string GetCell(IXLRow row, string canonical)
        {
            return mapping.TryGetValue(canonical, out var col) && col > 0 ? row.Cell(col).GetString().Trim() : string.Empty;
        }

        decimal GetDec(IXLRow row, string canonical)
        {
            var value = GetCell(row, canonical);
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv)) return inv;
            if (decimal.TryParse(value, NumberStyles.Any, new CultureInfo("pt-BR"), out var pt)) return pt;
            return 0m;
        }

        var lanes = new List<BidLane>();
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow))
        {
            var origin = GetCell(row, "Origin");
            var destination = GetCell(row, "Destination");
            var route = GetCell(row, "Route");

            if ((string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination)) && !string.IsNullOrWhiteSpace(route))
            {
                var split = SplitRoute(route);
                origin = split.Origin;
                destination = split.Destination;
            }

            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            lanes.Add(new BidLane
            {
                Origin = origin,
                Destination = destination,
                FreightType = GetCell(row, "FreightType"),
                VolumeForecast = GetDec(row, "VolumeForecast"),
                SlaRequirements = GetCell(row, "SlaRequirements"),
                VehicleType = GetCell(row, "VehicleType"),
                InsuranceRequirements = GetCell(row, "InsuranceRequirements"),
                PaymentTerms = GetCell(row, "PaymentTerms"),
                Region = GetCell(row, "Region")
            });
        }

        return lanes;
    }

    public ExcelAnalysisResult AnalyzeColumns(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        return AnalyzeWorkbook(workbook.Worksheets.First());
    }

    public ExcelAnalysisResult AnalyzeColumns(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        return AnalyzeWorkbook(workbook.Worksheets.First());
    }

    public List<TemplateHeaderMatch> MatchTemplate(IFormFile file, List<TemplateFieldDefinition> templateFields)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var headerRow = DetectHeaderRow(sheet);
        var headers = sheet.Row(headerRow).CellsUsed()
            .Select(c => new HeaderCandidate(c.GetString().Trim(), c.Address.ColumnNumber, Normalize(c.GetString())))
            .Where(x => !string.IsNullOrWhiteSpace(x.Raw))
            .ToList();

        var used = new HashSet<int>();
        var result = new List<TemplateHeaderMatch>();

        foreach (var field in templateFields.OrderBy(x => x.SortOrder))
        {
            var aliases = (field.Aliases ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            aliases.Add(field.Key);
            aliases.Add(field.DisplayName);
            CanonicalAliases.TryGetValue(field.Key, out var standardAliases);
            if (standardAliases is not null)
            {
                aliases.AddRange(standardAliases);
            }

            var best = FindBestHeaderForAliases(aliases, headers, used);
            if (best is { } m && m.Confidence >= 0.45m)
            {
                used.Add(m.Index);
                result.Add(new TemplateHeaderMatch(field.Key, field.DisplayName, field.IsRequired, m.Raw, Math.Round(m.Confidence, 2)));
            }
            else
            {
                result.Add(new TemplateHeaderMatch(field.Key, field.DisplayName, field.IsRequired, null, 0m));
            }
        }

        return result;
    }

    public byte[] BuildTemplateWorkbook(List<TemplateFieldDefinition> templateFields)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Template_BID");

        var ordered = templateFields.OrderBy(x => x.SortOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ws.Cell(1, i + 1).Value = ordered[i].DisplayName;
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static ExcelAnalysisResult AnalyzeWorkbook(IXLWorksheet sheet)
    {
        var headerRowNumber = DetectHeaderRow(sheet);
        var headerRow = sheet.Row(headerRowNumber);
        var headers = headerRow.CellsUsed()
            .Select(c => new HeaderCandidate(c.GetString().Trim(), c.Address.ColumnNumber, Normalize(c.GetString())))
            .Where(x => !string.IsNullOrWhiteSpace(x.Raw))
            .ToList();

        var matches = new List<ExcelColumnMatch>();
        var usedHeaders = new HashSet<int>();

        foreach (var canonical in CanonicalAliases.Keys)
        {
            var best = FindBestHeader(canonical, headers, usedHeaders);
            if (best is null || best.Value.Confidence < 0.55m)
            {
                continue;
            }

            usedHeaders.Add(best.Value.Index);
            matches.Add(new ExcelColumnMatch(canonical, best.Value.Raw, best.Value.Index, Math.Round(best.Value.Confidence, 2)));
        }

        var unmapped = headers.Where(h => !usedHeaders.Contains(h.Index)).Select(h => h.Raw).ToList();
        return new ExcelAnalysisResult(
            sheet.Name,
            sheet.LastRowUsed()?.RowNumber() ?? 0,
            sheet.LastColumnUsed()?.ColumnNumber() ?? 0,
            matches,
            unmapped);
    }

    private static Dictionary<string, int> MatchColumns(IXLWorksheet sheet)
    {
        var analysis = AnalyzeWorkbook(sheet);
        return analysis.Matches.ToDictionary(x => x.CanonicalField, x => x.ColumnIndex, StringComparer.OrdinalIgnoreCase);
    }

    private static int DetectHeaderRow(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var maxScan = Math.Min(lastRow, 20);
        var canonical = CanonicalAliases.Keys.ToList();
        var bestRow = 1;
        var bestScore = 0m;

        for (var r = 1; r <= maxScan; r++)
        {
            var row = sheet.Row(r);
            var headers = row.CellsUsed()
                .Select(c => new HeaderCandidate(c.GetString().Trim(), c.Address.ColumnNumber, Normalize(c.GetString())))
                .Where(x => !string.IsNullOrWhiteSpace(x.Raw))
                .ToList();

            if (!headers.Any())
            {
                continue;
            }

            var used = new HashSet<int>();
            decimal score = 0;
            foreach (var field in canonical)
            {
                var match = FindBestHeader(field, headers, used);
                if (match is { } m && m.Confidence >= 0.55m)
                {
                    used.Add(m.Index);
                    score += m.Confidence;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRow = r;
            }
        }

        return bestRow;
    }

    private static (string Raw, int Index, decimal Confidence)? FindBestHeader(
        string canonical,
        List<HeaderCandidate> headers,
        HashSet<int> usedHeaders)
    {
        var aliases = CanonicalAliases[canonical].Select(Normalize).ToList();
        (string Raw, int Index, decimal Confidence)? best = null;

        foreach (var h in headers)
        {
            if (usedHeaders.Contains(h.Index))
            {
                continue;
            }

            decimal score = 0m;
            foreach (var alias in aliases)
            {
                if (h.Normalized == alias)
                {
                    score = Math.Max(score, 1m);
                }
                else if (h.Normalized.Contains(alias) || alias.Contains(h.Normalized))
                {
                    score = Math.Max(score, 0.88m);
                }
                else
                {
                    var sim = Similarity(h.Normalized, alias);
                    score = Math.Max(score, sim);
                }
            }

            if (best is null || score > best.Value.Confidence)
            {
                best = (h.Raw, h.Index, score);
            }
        }

        return best;
    }

    private static (string Raw, int Index, decimal Confidence)? FindBestHeaderForAliases(
        IEnumerable<string> aliasesRaw,
        List<HeaderCandidate> headers,
        HashSet<int> usedHeaders)
    {
        var aliases = aliasesRaw
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct()
            .ToList();

        (string Raw, int Index, decimal Confidence)? best = null;
        foreach (var h in headers)
        {
            if (usedHeaders.Contains(h.Index))
            {
                continue;
            }

            decimal score = 0m;
            foreach (var alias in aliases)
            {
                if (h.Normalized == alias)
                {
                    score = Math.Max(score, 1m);
                }
                else if (h.Normalized.Contains(alias) || alias.Contains(h.Normalized))
                {
                    score = Math.Max(score, 0.88m);
                }
                else
                {
                    score = Math.Max(score, Similarity(h.Normalized, alias));
                }
            }

            if (best is null || score > best.Value.Confidence)
            {
                best = (h.Raw, h.Index, score);
            }
        }

        return best;
    }

    private static decimal Similarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0m;
        }

        var distance = Levenshtein(a, b);
        var max = Math.Max(a.Length, b.Length);
        if (max == 0)
        {
            return 1m;
        }

        return 1m - ((decimal)distance / max);
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public ExtractHeadersResult ExtractHeaders(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var headerRow = DetectHeaderRow(sheet);
        var headers = sheet.Row(headerRow).CellsUsed()
            .Select(c => new SourceHeader(c.Address.ColumnNumber, c.GetString().Trim()))
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .ToList();

        return new ExtractHeadersResult(
            sheet.Name,
            headerRow,
            sheet.LastRowUsed()?.RowNumber() ?? 0,
            headers);
    }

    public List<ExcelColumnMatch> AutoMapHeaders(List<SourceHeader> sourceHeaders, List<TemplateFieldDefinition> templateFields)
    {
        var candidates = sourceHeaders
            .Select(h => new HeaderCandidate(h.Name, h.ColumnIndex, Normalize(h.Name)))
            .ToList();

        var used = new HashSet<int>();
        var result = new List<ExcelColumnMatch>();

        foreach (var field in templateFields.OrderBy(f => f.SortOrder))
        {
            var aliases = (field.Aliases ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            aliases.Add(field.Key);
            aliases.Add(field.DisplayName);
            if (CanonicalAliases.TryGetValue(field.Key, out var std))
                aliases.AddRange(std);

            var best = FindBestHeaderForAliases(aliases, candidates, used);
            if (best is { } m && m.Confidence >= 0.45m)
            {
                used.Add(m.Index);
                result.Add(new ExcelColumnMatch(field.Key, m.Raw, m.Index, Math.Round(m.Confidence, 2)));
            }
            else
            {
                result.Add(new ExcelColumnMatch(field.Key, string.Empty, -1, 0m));
            }
        }

        return result;
    }

    public TransformPreviewResult TransformExcel(IFormFile file, int headerRow, List<ColumnMappingInput> mappings, int maxRows = 0)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        var colNames = mappings
            .Select(m => m.TargetFieldKey)
            .ToList();

        var rows = new List<Dictionary<string, string>>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var limit = maxRows > 0 ? Math.Min(lastRow, headerRow + maxRows) : lastRow;

        for (var r = headerRow + 1; r <= limit; r++)
        {
            var row = sheet.Row(r);
            var isEmpty = true;
            var dict = new Dictionary<string, string>();

            foreach (var m in mappings)
            {
                var value = m.SourceColumnIndex > 0
                    ? row.Cell(m.SourceColumnIndex).GetString().Trim()
                    : string.Empty;
                dict[m.TargetFieldKey] = value;
                if (!string.IsNullOrWhiteSpace(value)) isEmpty = false;
            }

            if (!isEmpty) rows.Add(dict);
        }

        return new TransformPreviewResult(rows.Count, colNames, rows);
    }

    public ExcelRawPreviewResult PreviewRaw(IFormFile file, int maxRows = 30)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        var totalRows = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var totalCols = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var detectedHeader = DetectHeaderRow(sheet);

        var columnLetters = new List<string>();
        for (var c = 1; c <= totalCols; c++)
            columnLetters.Add(ColumnIndexToLetter(c));

        var rows = new List<RawRowData>();
        var limit = Math.Min(totalRows, maxRows);

        for (var r = 1; r <= limit; r++)
        {
            var row = sheet.Row(r);
            var cells = new List<RawCellData>();
            for (var c = 1; c <= totalCols; c++)
            {
                cells.Add(new RawCellData(c, ColumnIndexToLetter(c), row.Cell(c).GetString().Trim()));
            }
            rows.Add(new RawRowData(r, cells));
        }

        return new ExcelRawPreviewResult(sheet.Name, totalRows, totalCols, detectedHeader, columnLetters, rows);
    }

    private static string ColumnIndexToLetter(int col)
    {
        var result = string.Empty;
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }

    private static (string Origin, string Destination) SplitRoute(string route)
    {
        var separators = new[] { "->", " x ", " X ", " / ", "-", "–", "—" };
        foreach (var sep in separators)
        {
            var parts = route.Split(sep, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0], parts[1]);
            }
        }

        return (string.Empty, string.Empty);
    }
}
