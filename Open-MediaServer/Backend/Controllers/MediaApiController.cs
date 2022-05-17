﻿using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using K4os.Compression.LZ4;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Open_MediaServer.Backend.Schema;
using Open_MediaServer.Content;
using Open_MediaServer.Database.Schema;
using Open_MediaServer.Utils;

namespace Open_MediaServer.Backend.Controllers;

[ApiController]
[Route("/api/[controller]")]
public class MediaApiController : ControllerBase
{
    // https://media.strateim.tech/RPf4BxA/Hello_World.mp4/
    [HttpGet("/api/thumbnail/")]
    public async Task<ActionResult> GetThumbnail(MediaSchema.MediaIdentity identity)
    {
        if (ModelState.IsValid)
        {
            if (!Program.ConfigManager.Config.Thumbnails)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var fileName = Path.GetFileNameWithoutExtension(identity.Name);
            var media = Program.Database.MediaDatabase.GetAsync<DatabaseSchema.Media>(media =>
                media.Id == identity.Id && media.Name == fileName).Result;

            if (media == null)
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }

            if (media.ContentType == ContentType.Other)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }

            byte[] thumbnail;
            if (media.ThumbnailPath == null)
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(media.ContentPath);

                if (media.ContentCompressed)
                {
                    bytes = LZ4Pickler.Unpickle(bytes);
                }

                thumbnail = ContentUtils.GetThumbnail(bytes, Program.ConfigManager.Config.ThumbnailSize?.Item1,
                    Program.ConfigManager.Config.ThumbnailSize?.Item2, media.ContentType);

                if (thumbnail == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }

                media.ThumbnailPath = Program.ContentManager.SaveThumbnail(thumbnail, media.Id, media.Name,
                    Program.ConfigManager.Config.ThumbnailType,
                    (ContentType) ContentUtils.GetContentType(media.Extension)!);
                await Program.Database.MediaDatabase.UpdateAsync(media);
            }
            else
            {
                thumbnail = await System.IO.File.ReadAllBytesAsync(media.ThumbnailPath);
            }

            var file = $"{media.Name}.png";
            return File(thumbnail, "image/png", file);
        }

        return StatusCode(StatusCodes.Status400BadRequest, ModelState);
    }


    [HttpGet("/api/media/")]
    public async Task<ActionResult> GetMedia(MediaSchema.MediaIdentity identity)
    {
        if (ModelState.IsValid)
        {
            var fileName = Path.GetFileNameWithoutExtension(identity.Name);
            var media = Program.Database.MediaDatabase.GetAsync<DatabaseSchema.Media>(media =>
                media.Id == identity.Id && media.Name == fileName).Result;

            if (media == null)
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(media.ContentPath);
            if (media.ContentCompressed)
            {
                bytes = LZ4Pickler.Unpickle(bytes);
            }

            var file = $"{media.Name}{media.Extension}";
            var fileContentType = new FileExtensionContentTypeProvider().TryGetContentType(file, out string contentType)
                ? contentType
                : "application/octet-stream";
            return File(bytes, fileContentType, file);
        }

        return StatusCode(StatusCodes.Status400BadRequest, ModelState);
    }
    
    [HttpGet("/api/mass/")]
    public async Task<MediaSchema.MediaReturnMass> GetMass(MediaSchema.MediaParameterMass parameterMass)
    {
        var mediaTable = await Program.Database.MediaDatabase.Table<DatabaseSchema.Media>().ToListAsync();
        mediaTable.RemoveAll(media => media.Public == false);
        if (parameterMass.Username != null)
        {
            var user = await Program.Database.UserDatabase.GetAsync<DatabaseSchema.User>(user => user.Username == parameterMass.Username);
            mediaTable.RemoveAll(media => media.Author != user);
        }
        if (parameterMass.Type != null)
        {
            mediaTable.RemoveAll(media => media.ContentType == parameterMass.Type);
        }
        var mediaIds = mediaTable.Select(media => media.Id).ToList();
        return new MediaSchema.MediaReturnMass()
        {
            Ids = mediaIds
        };
    }

    [HttpGet("/api/stats/")]
    public async Task<MediaSchema.MediaStats> GetStats()
    {
        var mediaTableQuery = Program.Database.MediaDatabase.Table<DatabaseSchema.Media>();
        var totalContent = await mediaTableQuery.CountAsync();
        var totalContentSize = mediaTableQuery.ToListAsync().Result.Sum(media => media.ContentSize);

        var statSchema = new MediaSchema.MediaStats()
        {
            ContentCount = totalContent,
            ContentTotalSize = totalContentSize,
            VideoContent = Program.ConfigManager.Config.AllowVideos,
            ImageContent = Program.ConfigManager.Config.AllowImages,
            OtherContent = Program.ConfigManager.Config.AllowOther
        };
        return statSchema;
    }

    [HttpPost("/api/upload/")]
    public async Task<ActionResult> PostUploadContent(MediaSchema.MediaUpload upload)
    {
        if (ModelState.IsValid)
        {
            if (Request.Cookies["user_session"] == null || !UserUtils.IsAuthed(Request.Cookies["user_session"]))
            {
                return StatusCode(StatusCodes.Status401Unauthorized);
            }
            DatabaseSchema.User user = await UserUtils.GetUser(Request.Cookies["user_session"]);

            if (user == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            
            var contentType = ContentUtils.GetContentType(upload.Extension);
            if (contentType == null)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }

            if (contentType == ContentType.Image && !Program.ConfigManager.Config.AllowImages
                || contentType == ContentType.Video && !Program.ConfigManager.Config.AllowVideos
                || contentType == ContentType.Other && !Program.ConfigManager.Config.AllowOther)
            {
                return StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            bool contentCompressed = Program.ConfigManager.Config.LosslessCompression;
            byte[] content;

            if (Program.ConfigManager.Config.LosslessCompression
                && LZ4Codec.MaximumOutputSize(upload.Content.Length) < upload.Content.Length)
            {
                content = LZ4Pickler.Pickle(upload.Content, Program.ConfigManager.Config.LosslessCompressionLevel);
            }
            else
            {
                content = upload.Content;
                contentCompressed = false;
            }

            var mediaSchema = new DatabaseSchema.Media()
            {
                // TODO: Actually generate a unique ID
                Id = StringUtils.RandomString(Program.ConfigManager.Config.UniqueIdLength),
                Name = upload.Name,
                Extension = upload.Extension,
                UploadDate = DateTime.UtcNow,
                ContentSize = content.Length,
                ContentCompressed = contentCompressed,
                ContentType = (ContentType) contentType,
                Public = upload.Public,
                Author = user
            };

            mediaSchema.ContentPath = Program.ContentManager.SaveContent(content, mediaSchema.Id, mediaSchema.Name,
                mediaSchema.Extension, (ContentType) contentType);

            if ((contentType == ContentType.Video || contentType == ContentType.Image) &&
                Program.ConfigManager.Config.Thumbnails && Program.ConfigManager.Config.PreComputeThumbnails)
            {
                var thumbnail = ContentUtils.GetThumbnail(upload.Content,
                    Program.ConfigManager.Config.ThumbnailSize?.Item1,
                    Program.ConfigManager.Config.ThumbnailSize?.Item2, (ContentType) contentType);
                if (thumbnail != null)
                {
                    mediaSchema.ThumbnailPath = Program.ContentManager.SaveThumbnail(thumbnail, mediaSchema.Id,
                        mediaSchema.Name, Program.ConfigManager.Config.ThumbnailType, (ContentType) contentType);
                }
                else
                {
                    Console.WriteLine(
                        $"Failed to pre compute thumbnail (ID: {mediaSchema.Id}, Name: {mediaSchema.Name}, Extension: {mediaSchema.Extension})");
                }
            }

            Console.WriteLine("Inserting media into sqlite db");
            await Program.Database.MediaDatabase.InsertAsync(mediaSchema);
            
            user.Uploads ??= new();
            user.Uploads.Add(mediaSchema.Id);
            await Program.Database.UserDatabase.UpdateAsync(user);
            
            string serializedJson = JsonSerializer.Serialize(mediaSchema, new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            Console.WriteLine(serializedJson);
            string serializedJson2 = JsonSerializer.Serialize(user, new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            Console.WriteLine(serializedJson2);
            
            return StatusCode(StatusCodes.Status200OK);
        }

        return StatusCode(StatusCodes.Status400BadRequest, ModelState);
    }

    [HttpPost("/api/delete/")]
    public async Task<ActionResult> PostDeleteContent(MediaSchema.MediaIdentity identity)
    {
        if (ModelState.IsValid)
        {
            if (Request.Cookies["user_session"] == null || !UserUtils.IsAuthed(Request.Cookies["user_session"]))
            {
                return StatusCode(StatusCodes.Status401Unauthorized);
            }
            DatabaseSchema.User user = await UserUtils.GetUser(Request.Cookies["user_session"]);
            if (user == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            var fileName = Path.GetFileNameWithoutExtension(identity.Name);
            var media = Program.Database.MediaDatabase.GetAsync<DatabaseSchema.Media>(media =>
                media.Id == identity.Id && media.Name == fileName).Result;

            if (media == null)
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }

            if (media.Author != user && !user.Admin)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            await Program.Database.MediaDatabase.DeleteAsync<DatabaseSchema.Media>(media);
            return StatusCode(StatusCodes.Status200OK);
        }
        
        return StatusCode(StatusCodes.Status400BadRequest, ModelState);
    }
}