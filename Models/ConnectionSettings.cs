using System.Text;

namespace SqlBackupBenchmark.Models;

public class ConnectionSettings
{
    public string Server { get; set; } = "localhost";
    public bool IntegratedSecurity { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool TrustServerCertificate { get; set; } = true;

    public string BuildConnectionString()
    {
        var sb = new StringBuilder();
        sb.Append($"Server={Server};");
        sb.Append(IntegratedSecurity
            ? "Integrated Security=true;"
            : $"User Id={UserId};Password={Password};");
        if (TrustServerCertificate)
            sb.Append("TrustServerCertificate=true;");
        sb.Append("Connection Timeout=5;");
        return sb.ToString();
    }

    public string Summary => IntegratedSecurity
        ? $"{Server} (Windows Auth)"
        : $"{Server} (SQL Login: {UserId})";

    public ConnectionSettings Clone() => new()
    {
        Server = Server,
        IntegratedSecurity = IntegratedSecurity,
        UserId = UserId,
        Password = Password,
        TrustServerCertificate = TrustServerCertificate
    };

    public void CopyFrom(ConnectionSettings other)
    {
        Server = other.Server;
        IntegratedSecurity = other.IntegratedSecurity;
        UserId = other.UserId;
        Password = other.Password;
        TrustServerCertificate = other.TrustServerCertificate;
    }
}
