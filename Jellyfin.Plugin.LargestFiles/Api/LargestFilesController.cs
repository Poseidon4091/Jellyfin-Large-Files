using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LargestFiles.Api;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LargestFiles")]
public class LargestFilesController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LargestFilesController> _logger;

    public LargestFilesController(ILibraryManager libraryManager, ILogger<LargestFilesController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns the Jellyfin libraries (Movies, TV Shows, Anime, etc) available to filter by.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LibraryDto>> GetLibraries()
    {
        try
        {
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => !string.IsNullOrEmpty(f.ItemId))
                .Select(f => new LibraryDto { Id = f.ItemId, Name = f.Name })
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("LargestFiles: found {Count} libraries", libraries.Count);

            return Ok(libraries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LargestFiles: failed to list libraries");
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Returns the largest items, grouped by library (Movies, TV Shows, Anime, etc).
    /// Movies are listed individually; TV/anime episodes are rolled up per series.
    /// </summary>
    /// <param name="perCategoryLimit">Max items to return per library category.</param>
    /// <param name="libraryId">Only include items from this library (Jellyfin collection folder id).</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<CategoryGroupDto>> Get(
        [FromQuery] int perCategoryLimit = 100,
        [FromQuery] Guid? libraryId = null)
    {
        var grouped = BuildGroups(perCategoryLimit, libraryId);
        return Ok(grouped);
    }

    /// <summary>
    /// Same data as <see cref="Get"/> but returned as a downloadable CSV file.
    /// </summary>
    /// <param name="perCategoryLimit">Max items to return per library category.</param>
    /// <param name="libraryId">Only include items from this library (Jellyfin collection folder id).</param>
    [HttpGet("Csv")]
    [Produces("text/csv")]
    public IActionResult GetCsv(
        [FromQuery] int perCategoryLimit = 100,
        [FromQuery] Guid? libraryId = null)
    {
        var grouped = BuildGroups(perCategoryLimit, libraryId);

        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,Type,FileCount,SizeBytes,SizeMB,Path");

        foreach (var group in grouped)
        {
            foreach (var item in group.Items)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    CsvEscape(group.Category),
                    CsvEscape(item.Name),
                    CsvEscape(item.Type),
                    item.FileCount.ToString(CultureInfo.InvariantCulture),
                    item.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    (item.SizeBytes / 1024d / 1024d).ToString("F2", CultureInfo.InvariantCulture),
                    CsvEscape(item.Path)
                }));
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"largest-files-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private List<CategoryGroupDto> BuildGroups(int perCategoryLimit, Guid? libraryId)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true
        };

        if (libraryId.HasValue && libraryId.Value != Guid.Empty)
        {
            query.AncestorIds = new[] { libraryId.Value };
        }

        var items = _libraryManager.GetItemList(query);
        var take = perCategoryLimit <= 0 ? 100 : perCategoryLimit;

        // Non-episode items (movies, standalone videos, etc) are listed individually.
        var standalone = new Dictionary<string, List<LargestFileDto>>(StringComparer.OrdinalIgnoreCase);

        // Episodes are rolled up per series: seriesId -> running total.
        var seriesTotals = new Dictionary<Guid, SeriesAccumulator>();

        foreach (var item in items)
        {
            var path = item.Path;
            if (string.IsNullOrEmpty(path) || item.IsFolder)
            {
                continue;
            }

            var size = GetPathSize(path);
            if (size <= 0)
            {
                continue;
            }

            if (item is Episode episode)
            {
                var seriesId = episode.SeriesId;
                if (seriesId == Guid.Empty)
                {
                    // No series link (orphaned episode) - fall back to treating it standalone.
                    AddStandalone(standalone, item, path, size);
                    continue;
                }

                if (!seriesTotals.TryGetValue(seriesId, out var acc))
                {
                    var series = _libraryManager.GetItemById(seriesId);
                    acc = new SeriesAccumulator
                    {
                        Name = series?.Name ?? episode.SeriesName ?? "Unknown Series",
                        Path = series?.Path ?? GetParentDirectory(path),
                        Category = GetCategory(series ?? episode)
                    };
                    seriesTotals[seriesId] = acc;
                }

                acc.SizeBytes += size;
                acc.FileCount++;
            }
            else
            {
                AddStandalone(standalone, item, path, size);
            }
        }

        var byCategory = new Dictionary<string, List<LargestFileDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in standalone)
        {
            byCategory[kvp.Key] = kvp.Value;
        }

        foreach (var acc in seriesTotals.Values)
        {
            if (!byCategory.TryGetValue(acc.Category, out var list))
            {
                list = new List<LargestFileDto>();
                byCategory[acc.Category] = list;
            }

            list.Add(new LargestFileDto
            {
                Name = acc.Name,
                Type = "Series",
                Path = acc.Path,
                SizeBytes = acc.SizeBytes,
                FileCount = acc.FileCount
            });
        }

        return byCategory
            .Select(kvp => new CategoryGroupDto
            {
                Category = kvp.Key,
                Items = kvp.Value
                    .OrderByDescending(i => i.SizeBytes)
                    .Take(take)
                    .ToList()
            })
            .Where(g => g.Items.Count > 0)
            .OrderBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddStandalone(Dictionary<string, List<LargestFileDto>> standalone, BaseItem item, string path, long size)
    {
        var category = GetCategory(item);

        if (!standalone.TryGetValue(category, out var list))
        {
            list = new List<LargestFileDto>();
            standalone[category] = list;
        }

        list.Add(new LargestFileDto
        {
            Name = item.Name,
            Type = item.GetType().Name,
            Path = path,
            SizeBytes = size,
            FileCount = 1
        });
    }

    private string GetCategory(BaseItem item)
    {
        var folders = _libraryManager.GetCollectionFolders(item);
        var name = folders?.FirstOrDefault()?.Name;
        return string.IsNullOrEmpty(name) ? "Other" : name;
    }

    private static string GetParentDirectory(string filePath)
    {
        try
        {
            return Path.GetDirectoryName(filePath) ?? filePath;
        }
        catch (ArgumentException)
        {
            return filePath;
        }
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static long GetPathSize(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

    private class SeriesAccumulator
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public int FileCount { get; set; }
    }
}

public class LargestFileDto
{
    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Path { get; set; }

    public long SizeBytes { get; set; }

    public int FileCount { get; set; }
}

public class CategoryGroupDto
{
    public string Category { get; set; } = string.Empty;

    public List<LargestFileDto> Items { get; set; } = new();
}

public class LibraryDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }
}
