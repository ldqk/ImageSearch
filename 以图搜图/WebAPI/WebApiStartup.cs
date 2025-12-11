using Masuit.Tools.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.IO;
using System.Reflection;

namespace 以图搜图.WebAPI;

public class WebApiStartup
{
    private static WebApplication? _application;
    public static bool ServerRunning { get; set; }

    public static Task Run(params string[] args)
    {
        var config = new IniFile("config.ini");
        var runServer = config.GetValue("Global", "RunServer", false);
        if (!runServer)
        {
            return Task.CompletedTask;
        }

        ServerRunning = true;
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "以图搜图 - 本地图像检索工具WPF版 by 懒得勤快 (评估版本)",
                Version = "v1"
            });
            // 设置 XML 注释文件路径
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            c.IncludeXmlComments(xmlPath);
        });
        builder.Services.AddCors(options => options.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
        var app = builder.Build();
        _application = app;
        app.UseCors();
        app.UseSwagger(options => options.RouteTemplate = "/openapi/{documentName}.json");
        app.MapScalarApiReference("/api");
        app.MapControllers();
        return app.RunAsync("http://0.0.0.0:" + config.GetValue("Global", "HttpPort", 5000));
    }

    public static async Task Stop()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }
}