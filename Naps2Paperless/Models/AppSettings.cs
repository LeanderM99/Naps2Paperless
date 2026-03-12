namespace Naps2Paperless.Models;

public class AppSettings
{
    public string Naps2Path { get; set; } = @"C:\Program Files\NAPS2\NAPS2.Console.exe";
    public string ProfileName { get; set; } = "PaperlessScan";
    public string ApiBaseUrl { get; set; } = "";
    public string ApiEndpoint { get; set; } = "api/documents/post_document/";
    public string ApiToken { get; set; } = "";
}
