using lucidRESUME.Collabora.DocumentOpeners;
using lucidRESUME.Collabora.Services;
using Microsoft.Extensions.DependencyInjection;

namespace lucidRESUME.Collabora;

public static class DocumentToolsServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentTools(this IServiceCollection services)
    {
        services.AddSingleton<LibreOfficeService>();
        services.AddSingleton<DocumentOpenerService>();
        return services;
    }
}
