﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevHome.SetupFlow.Common.WindowsPackageManager;
using DevHome.SetupFlow.Exceptions;
using DevHome.SetupFlow.Extensions;
using DevHome.SetupFlow.Models;
using Microsoft.Management.Deployment;
using Serilog;

namespace DevHome.SetupFlow.Services.WinGet;

/// <summary>
/// Finds packages using the Windows Package Manager (WinGet).
/// </summary>
internal sealed class WinGetPackageFinder : IWinGetPackageFinder
{
    private readonly ILogger _log = Log.ForContext("SourceContext", nameof(WinGetPackageFinder));
    private readonly WindowsPackageManagerFactory _wingetFactory;

    public WinGetPackageFinder(WindowsPackageManagerFactory wingetFactory)
    {
        _wingetFactory = wingetFactory;
    }

    /// <inheritdoc/>
    public async Task<IList<CatalogPackage>> SearchAsync(WinGetCatalog catalog, string query, uint limit)
    {
        if (catalog == null)
        {
            throw new CatalogNotInitializedException();
        }

        // Use default filter criteria for searching ('winget search {query}')
        _log.Information($"Searching for '{query}'. Result limit: {limit}");
        var filter = _wingetFactory.CreatePackageMatchFilter(PackageMatchField.CatalogDefault, PackageFieldMatchOption.ContainsCaseInsensitive, query);
        var options = _wingetFactory.CreateFindPackagesOptions();
        options.Selectors.Add(filter);
        options.ResultLimit = limit;
        return await GetPackagesInternalAsync(catalog, options);
    }

    /// <inheritdoc />
    public async Task<CatalogPackage> GetPackageAsync(WinGetCatalog catalog, string packageId)
    {
        var matches = await GetPackagesAsync(catalog, new HashSet<string> { packageId });
        if (matches.Count > 0)
        {
            return matches[0];
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<IList<CatalogPackage>> GetPackagesAsync(WinGetCatalog catalog, ISet<string> packageIds)
    {
        if (catalog == null)
        {
            throw new CatalogNotInitializedException();
        }

        _log.Information($"Getting packages: [{string.Join(", ", packageIds)}] from {catalog.GetDescriptiveName()}");

        // Skip search if set is empty
        if (!packageIds.Any())
        {
            _log.Warning($"{nameof(GetPackagesAsync)} received an empty set of package id. Skipping operation.");
            return new List<CatalogPackage>();
        }

        // 'winget' catalog supports getting multiple packages in a single request (optimized)
        var singleQuery = catalog.Type == WinGetCatalog.CatalogType.PredefinedWinget;
        return singleQuery ? await GetPackagesSingleQueryAsync(catalog, packageIds) : await GetPackagesMultiQueriesAsync(catalog, packageIds);
    }

    /// <inheritdoc />
    public bool IsElevationRequired(CatalogPackage package)
    {
        var packageId = package.Id;
        try
        {
            _log.Information($"Getting applicable installer info for package {packageId}");
            var installOptions = _wingetFactory.CreateInstallOptions();
            installOptions.PackageInstallScope = PackageInstallScope.Any;
            var applicableInstaller = package.DefaultInstallVersion.GetApplicableInstaller(installOptions);
            if (applicableInstaller != null)
            {
                _log.Information($"Elevation requirement = {applicableInstaller.ElevationRequirement} for package {packageId}");
                return applicableInstaller.ElevationRequirement == ElevationRequirement.ElevationRequired || applicableInstaller.ElevationRequirement == ElevationRequirement.ElevatesSelf;
            }

            _log.Warning($"No applicable installer info found for package {packageId}; defaulting to not requiring elevation");
            return false;
        }
        catch
        {
            _log.Warning($"Failed to get elevation requirement for package {packageId}; defaulting to not requiring elevation");
            return false;
        }
    }

    /// <summary>
    /// Get packages from the provided catalog using a single query
    /// </summary>
    /// <param name="catalog">Catalog from where the packages are queried</param>
    /// <param name="packageIds">Set of package ids</param>
    /// <returns>List of packages</returns>
    private async Task<IList<CatalogPackage>> GetPackagesSingleQueryAsync(WinGetCatalog catalog, ISet<string> packageIds)
    {
        var options = _wingetFactory.CreateFindPackagesOptions();
        foreach (var packageId in packageIds)
        {
            var filter = _wingetFactory.CreatePackageMatchFilter(PackageMatchField.Id, PackageFieldMatchOption.Equals, packageId);
            options.Selectors.Add(filter);
        }

        return await GetPackagesInternalAsync(catalog, options);
    }

    /// <summary>
    /// Get packages from the provided catalog by performing one query per requested package
    /// </summary>
    /// <param name="catalog">Catalog from where the packages are queried</param>
    /// <param name="packageIds">Set of package ids</param>
    /// <returns>List of packages</returns>
    private async Task<IList<CatalogPackage>> GetPackagesMultiQueriesAsync(WinGetCatalog catalog, ISet<string> packageIds)
    {
        var result = new List<CatalogPackage>();
        foreach (var packageId in packageIds)
        {
            var matches = await GetPackagesSingleQueryAsync(catalog, new HashSet<string> { packageId });
            if (matches.Count > 0)
            {
                result.Add(matches[0]);
            }
        }

        return result;
    }

    /// <summary>
    /// Get packages from the provided catalog
    /// </summary>
    /// <param name="catalog">Package catalog</param>
    /// <param name="options">Find packages options</param>
    /// <returns>List of packages</returns>
    private async Task<IList<CatalogPackage>> GetPackagesInternalAsync(WinGetCatalog catalog, FindPackagesOptions options)
    {
        _log.Information($"Performing search on catalog {catalog.GetDescriptiveName()}");
        var findResult = await catalog.Catalog.FindPackagesAsync(options);
        if (findResult.Status != FindPackagesResultStatus.Ok)
        {
            _log.Error($"Failed to find packages with status {findResult.Status}");
            throw new FindPackagesException(findResult.Status);
        }

        // Cannot use foreach or LINQ for out-of-process IVector
        // Bug: https://github.com/microsoft/CsWinRT/issues/1205
        var result = new List<CatalogPackage>();
        for (var i = 0; i < findResult.Matches.Count; ++i)
        {
            result.Add(findResult.Matches[i].CatalogPackage);
        }

        _log.Information($"Found {result.Count} results from catalog {catalog.GetDescriptiveName()} [{string.Join(", ", result.Select(p => p.Id))}]");

        return result;
    }
}
