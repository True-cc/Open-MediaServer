﻿using System.Linq;
using System.Threading.Tasks;
using Open_MediaServer.Database.Schema;

namespace Open_MediaServer.Utils;

public static class UserUtils
{
    public static bool IsAuthed(string sessionKey)
    {
        return Program.Database.UserDatabase
            .GetAsync<DatabaseSchema.User>(user => user.SessionKey == sessionKey).Result.SessionKey == sessionKey;
    }
    
    public static async Task<DatabaseSchema.User> GetUser(string sessionKey)
    {
        return await Program.Database.UserDatabase
            .GetAsync<DatabaseSchema.User>(user => user.SessionKey == sessionKey);
    }
}