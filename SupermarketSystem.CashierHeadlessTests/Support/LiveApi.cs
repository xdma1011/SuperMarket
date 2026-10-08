using System.Net;
using System.Net.Sockets;
using SupermarketSystem.IntegrationTests;

namespace SupermarketSystem.CashierHeadlessTests.Support;

/// <summary>
/// الـAPI الحقيقي (نفس Program.cs) على Kestrel بمنفذ حقيقي - ApiClient تبع الكاشير بيبني HttpClient خاص فيه (HttpClientHandler)،
/// فلازم سيرفر شبكة فعلي مش TestServer بالذاكرة. Stop() = السيرفر بيطفي فعلًا (connection refused = أوفلاين حقيقي)،
/// Start() = بيرجع على نفس المنفذ (نفس عنوان الكاشير). القاعدة نفسها (SQL Server) بتضل - زي انقطاع نت بالمحل.
/// </summary>
public sealed class LiveApi : IDisposable
{
    private readonly string _connectionString;
    private CustomWebApplicationFactory? _factory;

    public LiveApi(string connectionString)
    {
        _connectionString = connectionString;
        Port = FindFreePort();
    }

    public int Port { get; }

    /// <summary>نفس شكل AppConfig.ApiBaseUrl بالكاشير.</summary>
    public string BaseUrl => $"http://localhost:{Port}/api/v1";

    public bool IsRunning => _factory is not null;

    public void Start()
    {
        if (_factory is not null)
        {
            return;
        }

        var factory = new CustomWebApplicationFactory(_connectionString);
        factory.UseKestrel(Port);
        factory.StartServer();
        _factory = factory;
    }

    public void Stop()
    {
        var factory = _factory;
        _factory = null;
        factory?.Dispose();
    }

    public void Dispose() => Stop();

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
