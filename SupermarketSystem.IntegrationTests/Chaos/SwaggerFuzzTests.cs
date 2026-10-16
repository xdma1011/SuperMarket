using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// فَزّ عنيف مبني على مخطط Swagger نفسه (16/10/2026): لكل endpoint بالنظام بيُبنى طلب "سليم الشكل" من المخطط، وبعدين بتنحقن قيم عدائية حقل حقل
/// (نصوص ضخمة، SQL injection، يونيكود/Null chars، أرقام سالبة/ضخمة/صغيرة جدًا، GUID فاضي، مصفوفات ضخمة، JSON تالف، جسم فاضي...). القاعدة الوحيدة:
/// <b>ولا طلب يرجّع 5xx</b> (خطأ سيرفر = ثغرة أو انهيار)، ولا يعلّق (مهلة). 4xx بأنواعها مقبولة. مستخدم الأدمن (كل الصلاحيات) عشان الطلب يوصل لأعمق نقطة بالمعالج.
/// بيستثنى بس اللي إلو أثر خارج القاعدة (نسخ احتياطي/ترجيع) أو بيقتل جلسة الأدمن نفسها (خروج/جلسات).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SwaggerFuzzTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    public SwaggerFuzzTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture) { _output = output; }

    private static readonly string[] SkipPathFragments =
    {
        "/backups", "/auth/logout", "/auth/sessions", "/auth/login", "/auth/refresh", "bootstrap-admin", "/telegram/webhook", "/swagger", "/health"
    };

    private SwaggerModel _model = null!;

    /// <summary>كل مسارات الحقول القابلة للتحوير (ورقة بالـJSON) مع نوعها.</summary>
    private static IEnumerable<(string[] Path, JsonNode Value)> Leaves(JsonNode? node, List<string> path)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o.ToList())
                    foreach (var l in Leaves(kv.Value, [.. path, kv.Key])) yield return l;
                break;
            case JsonArray a when a.Count > 0:
                foreach (var l in Leaves(a[0], [.. path, "0"])) yield return l;
                break;
            case JsonValue v:
                yield return (path.ToArray(), v);
                break;
        }
    }

    private static IEnumerable<JsonNode?> Hostile(JsonNode original)
    {
        var kind = original.GetValueKind();
        if (kind == JsonValueKind.String)
        {
            var text = original.GetValue<string>();
            var isGuid = Guid.TryParse(text, out _);
            if (isGuid)
            {
                yield return Guid.Empty.ToString(); yield return "not-a-guid"; yield return null; yield return new string('f', 400);
                yield break;
            }

            yield return ""; yield return "   "; yield return new string('أ', 20000); yield return "'; DROP TABLE Users;--";
            yield return "' OR 1=1 --"; yield return "\u0000\u0001\u0002"; yield return "‮‭ RTL"; yield return "😀👨‍👩‍👧‍👦";
            yield return "<script>alert(1)</script>"; yield return "%00%27%22"; yield return "../../etc/passwd"; yield return null;
            yield return "2026-13-45T99:99:99Z"; yield return "9999999999999999999999"; yield return "-1";
        }
        else if (kind is JsonValueKind.Number)
        {
            yield return -1; yield return 0; yield return -0.0001; yield return 0.0000001; yield return 1e308; yield return 99999999999999999999.0;
            yield return int.MaxValue; yield return long.MinValue; yield return 0.1 + 0.2; yield return null; yield return "abc"; yield return true;
        }
        else if (kind is JsonValueKind.True or JsonValueKind.False)
        {
            yield return null; yield return "yes"; yield return 5; yield return new JsonArray();
        }
    }

    private static IEnumerable<string> WholeBodyHostile()
    {
        yield return "";
        yield return "null";
        yield return "[]";
        yield return "\"str\"";
        yield return "12345";
        yield return "{";
        yield return "{\"a\":";
        yield return "{\"a\":1,\"a\":2}";
        yield return new string('[', 3000);
        yield return "{\"x\":" + new string('1', 5000) + "}";
        yield return "﻿{}";
    }

    private static void SetPath(JsonNode root, string[] path, JsonNode? value)
    {
        JsonNode current = root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            current = current is JsonArray arr ? arr[int.Parse(path[i])]! : current[path[i]]!;
        }

        var last = path[^1];
        if (current is JsonArray a) a[int.Parse(last)] = value; else current[last] = value;
    }

    [Fact]
    public async Task فز_عنيف_ولا_endpoint_بيرجع_خطأ_سيرفر_5xx_تحت_قيم_عدائية()
    {
        // أدمن واحد؛ نحمّل المخطط من نفس الـAPI.
        var admin = await CreateAuthenticatedClientAsync();
        var swagger = await admin.GetAsync("/swagger/v1/swagger.json");
        Assert.True(swagger.IsSuccessStatusCode, "المخطط لازم يتحمّل");
        _model = new SwaggerModel((JsonObject)JsonNode.Parse(await swagger.Content.ReadAsStringAsync())!);

        var operations = _model.LoadOperations()
            .Where(o => !SkipPathFragments.Any(f => o.Path.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _output.WriteLine($"عدد العمليات: {operations.Count}");
        Assert.True(operations.Count > 100, "المخطط لازم يغطي كل الـendpoints");

        var failures = new List<string>();
        var requestCount = 0;

        async Task SendAsync(SwaggerModel.Operation op, string url, string? body, string label)
        {
            requestCount++;
            using var request = new HttpRequestMessage(new HttpMethod(op.Method), url);
            if (body is not null && op.Method != "GET")
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await admin.SendAsync(request, cts.Token);
                if ((int)response.StatusCode >= 500)
                {
                    var text = await response.Content.ReadAsStringAsync();
                    failures.Add($"{(int)response.StatusCode} {op.Method} {op.Path} [{label}] -> {(text.Length > 220 ? text[..220] : text)}");
                }
            }
            catch (OperationCanceledException)
            {
                failures.Add($"TIMEOUT {op.Method} {op.Path} [{label}]");
            }
            catch (Exception ex)
            {
                failures.Add($"EXCEPTION {op.Method} {op.Path} [{label}] -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var op in operations)
        {
            var baseline = _model.Sample(op.BodySchema);
            var url = _model.FillPath(op);

            // 1) الطلب السليم أولًا + أجسام كاملة عدائية.
            await SendAsync(op, url, baseline?.ToJsonString() ?? (op.Method == "GET" ? null : "{}"), "baseline");
            if (op.Method != "GET")
            {
                foreach (var whole in WholeBodyHostile()) await SendAsync(op, url, whole, "body:" + (whole.Length > 20 ? whole[..20] : whole));
            }

            // 2) حقول الجسم حقل حقل.
            if (baseline is not null)
            {
                foreach (var (path, value) in Leaves(baseline, []).Take(14))
                {
                    foreach (var hostile in Hostile(value))
                    {
                        var mutated = baseline.DeepClone();
                        SetPath(mutated, path, hostile?.DeepClone());
                        await SendAsync(op, url, mutated.ToJsonString(), $"{string.Join('.', path)}={Trim(hostile)}");
                    }
                }
            }

            // 3) باراميترات query/path عدائية.
            foreach (var p in op.Parameters.Where(p => p.In is "query" or "path"))
            {
                var sample = _model.Sample(p.Schema) ?? "x";
                foreach (var hostile in Hostile(sample.DeepClone()).Take(10))
                {
                    var text = hostile?.ToString() ?? "";
                    // TestServer نفسه بيرمي لـ%00 بالمسار (Kestrel الحقيقي بيرجع 400 قبل ما يوصل للتطبيق) - مش خلل بالسيرفر.
                    if (p.In == "path" && text.Contains('\0')) continue;
                    var path = op.Path;
                    foreach (var pp in op.Parameters.Where(x => x.In == "path"))
                    {
                        var v = pp.Name == p.Name ? text : (_model.Sample(pp.Schema)?.ToString() ?? "x");
                        path = path.Replace("{" + pp.Name + "}", Uri.EscapeDataString(v));
                    }

                    var query = op.Parameters.Where(x => x.In == "query")
                        .Select(x => $"{x.Name}={Uri.EscapeDataString(x.Name == p.Name ? text : (_model.Sample(x.Schema)?.ToString() ?? "x"))}");
                    var finalUrl = path + (query.Any() ? "?" + string.Join("&", query) : "");
                    await SendAsync(op, finalUrl, baseline?.ToJsonString(), $"param:{p.Name}={Trim(hostile)}");
                }
            }
        }

        _output.WriteLine($"عدد الطلبات: {requestCount}، الإخفاقات: {failures.Count}");
        var grouped = failures.GroupBy(f => f.Split(" [")[0]).Select(g => g.First() + (g.Count() > 1 ? $"   (+{g.Count() - 1} غيرها بنفس الـendpoint)" : "")).ToList();
        foreach (var f in grouped) _output.WriteLine(f);
        Assert.True(grouped.Count == 0, $"{grouped.Count} endpoint رجّع 5xx/مهلة:\n" + string.Join("\n", grouped.Take(60)));
    }

    private static string Trim(JsonNode? node)
    {
        var s = node?.ToJsonString() ?? "null";
        return s.Length > 30 ? s[..30] + "…" : s;
    }
}
