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

namespace Jellyfin.Plugin.LargestFiles.Api;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LargestFiles")]
public class LargestFilesController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    public LargestFilesController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Returns the largest files, grouped by library (Movies, TV Shows, Anime, etc).
    /// </summary>
    /// <param name="perCategoryLimit">Max items to return per library category.</param>
    /// <param name="minSizeMb">Only include items at or above this size, in MB.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<CategoryGroupDto>> Get(
        [FromQuery] int perCategoryLimit = 100,
        [FromQuery] double minSizeMb = 0)
    {
        var grouped = BuildGroups(perCategoryLimit, minSizeMb);
        return Ok(grouped);
    }

    /// <summary>
    /// Same data as <see cref="Get"/> but returned as a downloadable CSV file.
    /// </summary>
    /// <param name="perCategoryLimit">Max items to return per library category.</param>
    /// <param name="minSizeMb">Only include items at or above this size, in MB.</param>
    [HttpGet("Csv")]
    [Produces("text/csv")]
    public IActionResult GetCsv(
        [FromQuery] int perCategoryLimit = 100,
        [FromQuery] double minSizeMb = 0)
    {
        var grouped = BuildGroups(perCategoryLimit, minSizeMb);

        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,SeriesName,Type,SizeBytes,SizeMB,Path");

        foreach (var group in grouped)
        {
            foreach (var item in group.Items)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    CsvEscape(group.Category),
                    CsvEscape(item.Name),
                    CsvEscape(item.SeriesName),
                    CsvEscape(item.Type),
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

    private List<CategoryGroupDto> BuildGroups(int perCategoryLimit, double minSizeMb)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true
        };

        var items = _libraryManager.GetItemList(query);
        var minBytes = (long)(minSizeMb * 1024 * 1024);
        var take = perCategoryLimit <= 0 ? 100 : perCategoryLimit;

        var byCategory = new Dictionary<string, List<LargestFileDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var path = item.Path;
            if (string.IsNullOrEmpty(path) || item.IsFolder)
            {
                continue;
            }

            var size = GetPathSize(path);
            if (size <= 0 || size < minBytes)
            {
                continue;
            }

            var category = GetCategory(item);

            if (!byCategory.TryGetValue(category, out var list))
            {
                list = new List<LargestFileDto>();
                byCategory[category] = list;
            }

            list.Add(new LargestFileDto
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.GetType().Name,
                Path = path,
                SizeBytes = size,
                SeriesName = (item as Episode)?.SeriesName
            });
        }

        return byCategory
            .Select(kvp => new CategoryGroupDto
            {
                Category = kvp.Key,
                Items = kvp.Value.OrderByDescending(i => i.SizeBytes).Take(take).ToList()
            })
            .OrderBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string GetCategory(BaseItem item)
    {
        var folders = _libraryManager.GetCollectionFolders(item);
        var name = folders?.FirstOrDefault()?.Name;
        return string.IsNullOrEmpty(name) ? "Other" : name;
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
}

public class LargestFileDto
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Path { get; set; }

    public long SizeBytes { get; set; }

    public string? SeriesName { get; set; }
}

public class CategoryGroupDto
{
    public string Category { get; set; } = string.Empty;

    public List<LargestFileDto> Items { get; set; } = new();
}
