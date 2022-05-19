﻿using System.ComponentModel.DataAnnotations;
using SQLiteNetExtensions.Attributes;

namespace Open_MediaServer.Backend.Schema;

public class UserSchema
{
    public class User
    {
        [Required]
        public string Username { get; set; }
        [Required]
        public string Password { get; set; }
    }
    
    public class UserDelete
    {
        [OneToOne]
        [Required]
        public User User { get; set; }
        [Required]
        public bool DeleteMedia { get; set; }
    }
}