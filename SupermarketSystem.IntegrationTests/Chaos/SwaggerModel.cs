using System.Text.Json.Nodes;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>مخطط Swagger محمَّل: بيبني عمليات وجسم "سليم الشكل" لكل endpoint (مشترك بين الفز ومصفوفة الصلاحيات).</summary>
internal sealed class SwaggerModel
{
    public sealed record Operation(string Method, string Path, List<(string Name, string In, JsonNode? Schema)> Parameters, JsonNode? BodySchema, bool Multipart = false);

    private readonly JsonObject _doc;

    public SwaggerModel(JsonObject doc) { _doc = doc; }



    private JsonNode? Resolve(JsonNode? schema)
    {
        for (var depth = 0; depth < 8 && schema is JsonObject o && o["$ref"] is JsonValue r; depth++)
        {
            var name = r.GetValue<string>().Split('/').Last();
            schema = _doc["components"]!["schemas"]![name];
        }

        return schema;
    }

    public JsonNode? Sample(JsonNode? schemaNode, int depth = 0)
    {
        var schema = Resolve(schemaNode) as JsonObject;
        if (schema is null || depth > 4) return null;
        if (schema["allOf"] is JsonArray allOf && allOf.Count > 0) return Sample(allOf[0], depth);
        if (schema["oneOf"] is JsonArray oneOf && oneOf.Count > 0) return Sample(oneOf[0], depth);
        if (schema["anyOf"] is JsonArray anyOf && anyOf.Count > 0) return Sample(anyOf.FirstOrDefault(x => x is JsonObject xo && xo["type"]?.GetValue<string>() != "null") ?? anyOf[0], depth);

        if (schema["enum"] is JsonArray en && en.Count > 0) return en[0]!.DeepClone();
        var type = schema["type"]?.ToString();
        var format = schema["format"]?.ToString();
        switch (type)
        {
            case "string":
                return format switch
                {
                    "uuid" => Guid.NewGuid().ToString(),
                    "date-time" => DateTime.UtcNow.ToString("o"),
                    "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    "time" => "10:00:00",
                    _ => "نص"
                };
            case "integer": return 1;
            case "number": return 1.5;
            case "boolean": return false;
            case "array": return new JsonArray(Sample(schema["items"], depth + 1));
            default:
                if (schema["properties"] is JsonObject props)
                {
                    var obj = new JsonObject();
                    foreach (var p in props) obj[p.Key] = Sample(p.Value, depth + 1);
                    return obj;
                }

                return new JsonObject();
        }
    }

    public string FillPath(Operation op, Guid? idOverride = null)
    {
        var path = op.Path;
        foreach (var (name, location, schema) in op.Parameters.Where(p => p.In == "path"))
        {
            var sample = Sample(schema)?.ToString() ?? "x";
            if (idOverride is { } g && Guid.TryParse(sample, out _)) sample = g.ToString();
            path = path.Replace("{" + name + "}", Uri.EscapeDataString(sample));
        }

        var query = op.Parameters.Where(p => p.In == "query").Select(p => $"{p.Name}={Uri.EscapeDataString(Sample(p.Schema)?.ToString() ?? "x")}").ToList();
        return query.Count > 0 ? path + "?" + string.Join("&", query) : path;
    }

    public List<Operation> LoadOperations()
    {
        var ops = new List<Operation>();
        foreach (var pathEntry in (JsonObject)_doc["paths"]!)
        {
            foreach (var methodEntry in (JsonObject)pathEntry.Value!)
            {
                var method = methodEntry.Key.ToUpperInvariant();
                if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH")) continue;
                var op = (JsonObject)methodEntry.Value!;
                var parameters = new List<(string, string, JsonNode?)>();
                foreach (var p in (op["parameters"] as JsonArray) ?? [])
                {
                    parameters.Add((p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>(), p["schema"]));
                }

                JsonNode? body = op["requestBody"]?["content"]?["application/json"]?["schema"];
                var multipart = op["requestBody"]?["content"]?["multipart/form-data"] is not null;
                ops.Add(new Operation(method, pathEntry.Key, parameters, body, multipart));
            }
        }

        return ops;
    }
}
