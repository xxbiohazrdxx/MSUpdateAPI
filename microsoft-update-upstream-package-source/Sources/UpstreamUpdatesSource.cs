// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Source
{
    /// <summary>
    /// Retrieves updates from the Microsoft Update catalog or a WSUS upstream server.
    /// </summary>
    public class UpstreamUpdatesSource : IMetadataSource
    {
        private readonly UpstreamServerClient _Client;
        private UpstreamSourceFilter _Filter;

        private IEnumerable<MicrosoftUpdatePackageIdentity> _Identities;

        /// <summary>
        /// Progress indicator during metadata copy operations
        /// </summary>
        public event EventHandler<PackageStoreEventArgs> MetadataCopyProgress;

#pragma warning disable 0067
        /// <summary>
        /// Progress indicator during source open operations. Not used by UpstreamUpdatesSource.
        /// </summary>
        public event EventHandler<PackageStoreEventArgs> OpenProgress;

        /// <summary>
        /// Create a new MicrosoftUpdate package source that retrieves updates from the specified endpoint
        /// </summary>
        /// <param name="upstreamEndpoint">Endpoint to get updates from</param>
        /// <param name="filter">Filter to apply when retrieving updates from this source.</param>
        public UpstreamUpdatesSource(Endpoint upstreamEndpoint, UpstreamSourceFilter filter)
        {
            _Client = new UpstreamServerClient(upstreamEndpoint);
            _Filter = filter;
        }

        private async Task RetrievePackageIdentities()
        {
            if (_Identities == null)
            {
                _Identities = await _Client.GetUpdateIds(_Filter);
                //_Identities.Sort();
            }
        }


		/// <summary>
		/// Retrieves products from the upstream source
		/// </summary>
		/// <param name="cancelToken">Cancellation token</param>
		/// <param name="excludedPackageIds">A list of GUIDs to exclude from the retrieval</param>
		/// <param name="immediatelyReleaseMetadata">If true, metadata XML is immediately discarded once the update is parsed</param>
		/// <returns>List of Microsoft Update updates</returns>
		public async IAsyncEnumerable<MicrosoftUpdatePackage> GetUpdates(CancellationToken cancelToken, IEnumerable<Guid> excludedPackageIds = null)
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

					var retrievedBatch = _Client.GetUpdateDataForIds(batch);

                    await foreach(var retrieved in retrievedBatch)
                    {
                        Interlocked.Increment(ref progressArgs.Current);
						MetadataCopyProgress?.Invoke(this, progressArgs);

						yield return retrieved;
                    }
				}
            }
			else
			{
				MetadataCopyProgress?.Invoke(this, new PackageStoreEventArgs());
			}

            yield return null;
		}

		/// <inheritdoc cref="IMetadataSource.CopyTo(IMetadataSink, CancellationToken)"/>
		public async Task CopyTo(IMetadataSink destination, CancellationToken cancelToken)
        {
            await RetrievePackageIdentities();

            IEnumerable<MicrosoftUpdatePackageIdentity> unavailableUpdates;

            if (destination is IMetadataStore destinationBaseline)
            {
                 unavailableUpdates = _Identities.Where(u => !destinationBaseline.ContainsPackage(u));
            }
            else
            {
                unavailableUpdates = _Identities;
            }

            if (unavailableUpdates.Any())
            {
                var progressArgs = new PackageStoreEventArgs() { Total = unavailableUpdates.Count(), Current = 0 };
                var batches = unavailableUpdates.Chunk(50);
                
                MetadataCopyProgress?.Invoke(this, progressArgs);
                foreach(var batch in batches)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var retrievedPackages = _Client.GetUpdateDataForIds(batch);

                    await foreach(var retrievedPackage in retrievedPackages)
                    {
                        destination.AddPackage(retrievedPackage);

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

        /// <inheritdoc cref="IMetadataSource.CopyTo(IMetadataSink, IMetadataFilter, CancellationToken)"/>
        public async Task CopyTo(IMetadataSink destination, IMetadataFilter filter, CancellationToken cancelToken)
        {
            if (filter is UpstreamSourceFilter categoriesFilter)
            {
                _Filter = categoriesFilter;
            }

            await CopyTo(destination, cancelToken);
        }

        /// <summary>
        /// Not implemented for an upstream update source
        /// </summary>
        /// <param name="packageIdentity">Identity of update to retrieve</param>
        /// <returns>Update metadata as stream</returns>
        /// <exception cref="NotImplementedException"></exception>
        public Stream GetMetadata(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented for an upstream update source
        /// </summary>
        /// <param name="packageIdentity">Indentity of package to lookup</param>
        /// <returns>True if found, false otherwise</returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool ContainsMetadata(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented for an upstream update source
        /// </summary>
        /// <typeparam name="T">Type of file to retrieve.</typeparam>
        /// <param name="packageIdentity">Identity of the package to retrieve files for.</param>
        /// <returns>List of files in the package</returns>
        /// <exception cref="NotImplementedException"></exception>
        public List<T> GetFiles<T>(IPackageIdentity packageIdentity)
        {
            throw new NotImplementedException();
        }
    }
}
