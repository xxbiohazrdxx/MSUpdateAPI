// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage;
using System.Threading.Tasks;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Source
{
    /// <summary>
    /// Retrieves all available categories from the Microsoft Update catalog.
    /// <para>
    /// Categories consist of Detectoids, Products and Classifications.
    /// </para>
    /// <para>
    /// Products and classifications are used to categorize updates; they are useful as filters for selectively
    /// sync'ing updates from an upstream server.
    /// </para>
    /// </summary>
    public class UpstreamCategoriesSource : IMetadataSource
    {
        private readonly UpstreamServerClient _Client;

        private IEnumerable<MicrosoftUpdatePackageIdentity> _Identities;

        /// <summary>
        /// Progress indicator during metadata copy operations
        /// </summary>
        public event EventHandler<PackageStoreEventArgs> MetadataCopyProgress;

#pragma warning disable 0067
        /// <summary>
        /// Progress indicator during source open operations. Not used by UpstreamCategoriesSource.
        /// </summary>
        public event EventHandler<PackageStoreEventArgs> OpenProgress;

        /// <summary>
        /// Create a new MicrosoftUpdate package source that retrieves updates from the specified endpoint
        /// </summary>
        /// <param name="upstreamEndpoint">Endpoint to get updates from</param>
        public UpstreamCategoriesSource(Endpoint upstreamEndpoint)
        {
            _Client = new UpstreamServerClient(upstreamEndpoint);
        }

        private async Task RetrievePackageIdentities()
        {
            if (_Identities == null)
            {
                _Identities = await _Client.GetCategoryIds();
                //_Identities.Sort();
            }
        }

		/// <summary>
		/// Retrieves categories from the upstream source
		/// </summary>
		/// <param name="cancelToken">Cancellation token</param>
		/// <param name="excludedPackageIds">A list of GUIDs to exclude from the retrieval</param>
		/// <returns>List of Microsoft Update categories</returns>
		public async IAsyncEnumerable<MicrosoftUpdatePackage> GetCategories(CancellationToken cancelToken, IEnumerable<Guid> excludedPackageIds = null)
        {
			excludedPackageIds ??= Array.Empty<Guid>();

            await RetrievePackageIdentities();

            var unavailableUpdates = _Identities
                .Where(u => !excludedPackageIds.Any(e => u.ID == e));

            if (unavailableUpdates.Any())
            {
                var batches = unavailableUpdates.Chunk(50);

                var progressArgs = new PackageStoreEventArgs() { Total = unavailableUpdates.Count(), Current = 0 };
                foreach(var batch in batches)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        break;
                    }
					
					var retrievedPackages = _Client.GetUpdateDataForIds(batch);

                    await foreach(var updatePackage in retrievedPackages)
                    {
						Interlocked.Increment(ref progressArgs.Current);
						MetadataCopyProgress?.Invoke(this, progressArgs);

						yield return updatePackage;
                    }
                }
            }
			else
			{
				MetadataCopyProgress?.Invoke(this, new PackageStoreEventArgs());
			}

			yield break;
        }

        /// <inheritdoc cref="IMetadataSource.CopyTo(IMetadataSink, CancellationToken)"/>
        public async Task CopyTo(IMetadataSink destination, CancellationToken cancelToken)
        {
            await RetrievePackageIdentities();

            IEnumerable<MicrosoftUpdatePackageIdentity> unavailableUpdates;

            if (destination is IMetadataStore baseline)
            {
                unavailableUpdates = new List<MicrosoftUpdatePackageIdentity>();
                unavailableUpdates = _Identities.Where(id => !baseline.ContainsPackage(id));
            }
            else
            {
                unavailableUpdates = _Identities;
            }

            if (unavailableUpdates.Any())
            {
                var batches = unavailableUpdates.Chunk(50);

                var progressArgs = new PackageStoreEventArgs() { Total = unavailableUpdates.Count(), Current = 0 };

                foreach(var batch in batches)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var retrievedPackages = _Client.GetUpdateDataForIds(batch);

                    await foreach(var updatePackage in retrievedPackages)
                    {
                        destination.AddPackage(updatePackage);

						Interlocked.Increment(ref progressArgs.Current);
						MetadataCopyProgress?.Invoke(this, progressArgs);
					}  
                }
            }
            else
            {
                MetadataCopyProgress?.Invoke(this, new PackageStoreEventArgs());
            }
        }

        /// <summary>
        /// Filtered copy not implemented for the categories source as categories cannot be filtered when
        /// sync'ing from an upstream server.
        /// </summary>
        /// <param name="destination">Destination store for the retrieved metadata</param>
        /// <param name="filter">Filter to apply during the copy operation</param>
        /// <param name="cancelToken">Cancellation token</param>
        /// <exception cref="NotImplementedException"></exception>
        public void CopyTo(IMetadataSink destination, IMetadataFilter filter, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented for an upstream categories source
        /// </summary>
        /// <param name="packageIdentity">Identity of the category to retrieve</param>
        /// <returns>Category metadata as stream</returns>
        /// <exception cref="NotImplementedException"></exception>
        public Stream GetMetadata(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented for an upstream categories source
        /// </summary>
        /// <param name="packageIdentity">Indentity of category to lookup</param>
        /// <returns>True if found, false otherwise</returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool ContainsMetadata(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented for an upstream update source. Also, do not contain files.
        /// </summary>
        /// <typeparam name="T">Type of file to retrieve.</typeparam>
        /// <param name="packageIdentity">Identity of the category to retrieve files for.</param>
        /// <returns>List of files in the category</returns>
        /// <exception cref="NotImplementedException"></exception>
        public List<T> GetFiles<T>(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }
    }
}
