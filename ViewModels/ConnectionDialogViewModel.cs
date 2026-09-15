using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.ViewModels;

public partial class ConnectionDialogViewModel : ViewModelBase
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F44747"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#888888"));

    [ObservableProperty] private string _server;
    [ObservableProperty] private bool _integratedSecurity;
    [ObservableProperty] private string _userId;
    [ObservableProperty] private string _password;
    [ObservableProperty] private bool _trustServerCertificate;
    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private bool _isTesting;

    public bool UseSqlLogin
    {
        get => !IntegratedSecurity;
        set => IntegratedSecurity = !value;
    }

    public IBrush TestStatusColor => TestStatus switch
    {
        _ when TestStatus.StartsWith("Connected") => OkBrush,
        _ when TestStatus.StartsWith("Failed") => ErrorBrush,
        _ => NeutralBrush
    };

    public ConnectionDialogViewModel(ConnectionSettings settings)
    {
        _server = settings.Server;
        _integratedSecurity = settings.IntegratedSecurity;
        _userId = settings.UserId;
        _password = settings.Password;
        _trustServerCertificate = settings.TrustServerCertificate;
    }

    partial void OnIntegratedSecurityChanged(bool value) => OnPropertyChanged(nameof(UseSqlLogin));

    partial void OnTestStatusChanged(string value) => OnPropertyChanged(nameof(TestStatusColor));

    public ConnectionSettings ToSettings() => new()
    {
        Server = Server,
        IntegratedSecurity = IntegratedSecurity,
        UserId = UserId,
        Password = Password,
        TrustServerCertificate = TrustServerCertificate
    };

    [RelayCommand]
    private async Task TestConnection()
    {
        IsTesting = true;
        TestStatus = "Testing...";
        try
        {
            await using var conn = new SqlConnection(ToSettings().BuildConnectionString());
            await conn.OpenAsync();
            TestStatus = $"Connected — SQL Server {conn.ServerVersion}";
        }
        catch (Exception ex)
        {
            TestStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}
