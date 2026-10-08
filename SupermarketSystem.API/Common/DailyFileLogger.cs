using System.Collections.Concurrent;
using System.Text;

namespace SupermarketSystem.API.Common;

/// <summary>
/// سجل بملف يومي (2-أ بند 4، 8/10/2026): الـconsole بس كان معناه شباك انسكّر = ضاع كل أثر لأي خطأ صار بالمحل.
/// مزوّد بسيط بلا أي حزمة جديدة (§1.2 - ما بنضيف Serilog لمجرد ملف نصي): ملف لكل يوم (`api-yyyy-MM-dd.log`) بمجلد قابل للضبط،
/// كتابة بخيط خلفي واحد (الطلب ما بيستنى القرص)، وحذف الملفات الأقدم من RetentionDays (سجلاتنا بس، مش أي ملف تاني).
/// فشل الكتابة (قرص ممتلئ، صلاحيات) ما بيوقف التطبيق ولا بيرمي استثناء للمستدعي.
/// الإعدادات (appsettings أو متغيّرات بيئة): `Logging:File:Enabled` (افتراضي true)، `Logging:File:Directory` (افتراضي مجلد `logs` جنب الـAPI)،
/// `Logging:File:RetentionDays` (افتراضي 30). مستوى التسجيل بيمشي على إعدادات `Logging:LogLevel` العادية (مزوّد "File").
/// </summary>
[ProviderAlias("File")]
public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;
    private DateOnly _lastCleanup = DateOnly.MinValue;

    public DailyFileLoggerProvider(string directory, int retentionDays)
    {
        _directory = directory;
        _retentionDays = retentionDays;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "daily-file-logger" };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Enqueue(string line)
    {
        // طابور ممتلئ (الكتابة معلّقة) = نرمي السطر بدل ما نعلّق الطلب.
        _queue.TryAdd(line);
    }

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var today = DateOnly.FromDateTime(DateTime.Now);
                File.AppendAllText(Path.Combine(_directory, $"api-{today:yyyy-MM-dd}.log"), line, Encoding.UTF8);
                if (_lastCleanup != today)
                {
                    _lastCleanup = today;
                    Cleanup(today);
                }
            }
            catch (Exception)
            {
                // السجل نفسه ما لازم يوقع التطبيق.
            }
        }
    }

    private void Cleanup(DateOnly today)
    {
        if (_retentionDays <= 0)
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_directory, "api-*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file)["api-".Length..];
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var date) && date < today.AddDays(-_retentionDays))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(3));
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly DailyFileLoggerProvider _provider;

        public FileLogger(string category, DailyFileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var level = logLevel switch
            {
                LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF",
                LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "???"
            };
            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level).Append("] ")
                .Append(_category).Append(": ").Append(formatter(state, exception)).AppendLine();
            if (exception is not null)
            {
                sb.AppendLine(exception.ToString());
            }

            _provider.Enqueue(sb.ToString());
        }
    }
}
