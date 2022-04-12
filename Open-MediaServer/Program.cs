using System;
using Microsoft.Extensions.Configuration;
using Open_MediaServer.Backend;
using Open_MediaServer.Config;
using Open_MediaServer.Content;
using Open_MediaServer.Database;
using Open_MediaServer.Frontend;

namespace Open_MediaServer;
public class Program
{
    public static ConfigManager ConfigManager;
    public static BackendServer Backend;
    public static FrontendServer Frontend;
    public static SqLite Database;
    public static ContentManager ContentManager;
    
    public static void Main(string[] args)
    {
        ConfigManager = new ConfigManager(ConfigManager.Config.WorkingDirectory ?? Environment.CurrentDirectory);
        ContentManager = new ContentManager(ConfigManager.Config.WorkingDirectory ?? Environment.CurrentDirectory);
        // Database = new SqLite(ConfigManager.Config.WorkingDirectory ?? Environment.CurrentDirectory);
        Backend = new BackendServer();
        // Frontend = new FrontendServer();
    }
}