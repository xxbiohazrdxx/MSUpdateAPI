// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UpdateServices.WebServices.ServerSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using System.Threading;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Source
{
	/// <summary>
	/// <para>
	/// Retrieves update metadata for expired updates from an upstream update server.
	/// </para>
	/// <para>
	/// This class should only be used for retrieving individual expired updates when their ID is known. For querying updates use <see cref="UpstreamUpdatesSource"/>. 
	/// For querying products and classifications, use <see cref="UpstreamCategoriesSource"/>
	/// </para>
	/// </summary>
	public class UpstreamServerClient
    {
        /// <summary>
        /// Gets the update server <see cref="Endpoint"/> this client connects to.
        /// </summary>
        /// <value>
        /// Update server <see cref="Endpoint"/>
        /// </value>
        public Endpoint UpstreamEndpoint { get; private set; }

        /// <summary>
        /// Client used to issue SOAP requests
        /// </summary>
        private readonly IServerSyncWebService ServerSyncClient;

        /// <summary>
        /// Cached access cookie. If not set in the constructor, a new access token will be obtained
        /// </summary>
        private ServiceAccessToken AccessToken;

        /// <summary>
        /// Service configuration data. Contains maximum query limits, etc.
        /// If not passed to the constructor, this class will retrieve it from the service
        /// </summary>
        private ServerSyncConfigData ConfigData;

        /// <summary>
        /// Raised on progress during a metadata query. Reports the current query stage.
        /// </summary>
        /// <value>Progress data</value>
        public event EventHandler<MetadataQueryProgress> MetadataQueryProgress;

        /// <summary>
        /// Initializes a new instance of UpstreamServerClient.
        /// </summary>
        /// <param name="upstreamEndpoint">The server endpoint this client will connect to.</param>
        public UpstreamServerClient(Endpoint upstreamEndpoint)
        {
            UpstreamEndpoint = upstreamEndpoint;

            var httpBindingWithTimeout = new System.ServiceModel.BasicHttpBinding()
            {
                ReceiveTimeout = new TimeSpan(0, 3, 0),
                SendTimeout = new TimeSpan(0, 3, 0),
                MaxBufferSize = int.MaxValue,
                ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max,
                MaxReceivedMessageSize = int.MaxValue,
                AllowCookies = true
            };

            var serviceEndpoint = new System.ServiceModel.EndpointAddress(UpstreamEndpoint.ServerSyncURI);
            if (serviceEndpoint.Uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                httpBindingWithTimeout.Security.Mode = System.ServiceModel.BasicHttpSecurityMode.Transport;
            }

            ServerSyncClient = new ServerSyncWebServiceClient(httpBindingWithTimeout, serviceEndpoint);
        }

        internal async Task RefreshAccessToken(string accountName, Guid? accountGuid)
        {
            var progress = new MetadataQueryProgress
            {
                CurrentTask = MetadataQueryStage.AuthenticateStart
            };
            MetadataQueryProgress?.Invoke(this, progress);

            var authenticator = new ClientAuthenticator(UpstreamEndpoint, accountName, accountGuid ?? new Guid());
            AccessToken = await authenticator.Authenticate(AccessToken);

            progress.CurrentTask = MetadataQueryStage.AuthenticateEnd;
            MetadataQueryProgress?.Invoke(this, progress);
        }

        internal async Task RefreshServerConfigData()
        {
            ConfigData = await GetServerConfigData();
        }

        /// <summary>
        /// Retrieves configuration data from the upstream server.
        /// </summary>
        /// <returns>Server configuration data</returns>
        public async Task<ServerSyncConfigData> GetServerConfigData()
        {
            await RefreshAccessToken(Guid.NewGuid().ToString(), Guid.NewGuid());
            var progress = new MetadataQueryProgress
            {
                CurrentTask = MetadataQueryStage.GetServerConfigStart
            };
            MetadataQueryProgress?.Invoke(this, progress);

            var result = await QueryConfigData();

            progress.CurrentTask = MetadataQueryStage.GetServerConfigEnd;
            MetadataQueryProgress?.Invoke(this, progress);

            return result;
        }

        private async Task<ServerSyncConfigData> QueryConfigData()
        {
            var configDataRequest = new GetConfigDataRequest
            {
                GetConfigData = new GetConfigDataRequestBody()
                {
                    configAnchor = null,
                    cookie = AccessToken.AccessCookie
                }
            };

            var configDataReply = await ServerSyncClient.GetConfigDataAsync(configDataRequest);
            if (configDataReply?.GetConfigDataResponse1?.GetConfigDataResult == null)
            {
                throw new Exception("Failed to get config data.");
            }

            return configDataReply.GetConfigDataResponse1.GetConfigDataResult;
        }

        internal async Task<IEnumerable<MicrosoftUpdatePackageIdentity>> GetCategoryIds(string oldAnchor = null)
        {
			await RefreshTokenAndConfig();

			// Create a request for categories
			var revisionIdRequest = new GetRevisionIdListRequest
            {
                GetRevisionIdList = new GetRevisionIdListRequestBody()
                {
                    cookie = AccessToken.AccessCookie,
                    filter = new ServerSyncFilter()
                }
            };

            if (!string.IsNullOrEmpty(oldAnchor))
            {
                revisionIdRequest.GetRevisionIdList.filter.Anchor = oldAnchor;
            }

            // GetConfig must be true to request just categories
            revisionIdRequest.GetRevisionIdList.filter.GetConfig = true;

            var revisionsIdReply = await ServerSyncClient.GetRevisionIdListAsync(revisionIdRequest);
            if (revisionsIdReply?.GetRevisionIdListResponse1?.GetRevisionIdListResult == null)
            {
                throw new Exception("Failed to get revision ID list");
            }

            // Return IDs and the anchor for this query. The anchor can be used to get a delta list in the future.
            return revisionsIdReply
                .GetRevisionIdListResponse1
                .GetRevisionIdListResult
                .NewRevisions
                .Select(rawId => new MicrosoftUpdatePackageIdentity(rawId.UpdateID, rawId.RevisionNumber));
        }

        internal async Task<IEnumerable<MicrosoftUpdatePackageIdentity>> GetUpdateIds(UpstreamSourceFilter updatesFilter)
        {
			await RefreshTokenAndConfig();

			// Create a request for categories
			var revisionIdRequest = new GetRevisionIdListRequest
            {
                GetRevisionIdList = new GetRevisionIdListRequestBody()
                {
                    cookie = AccessToken.AccessCookie,
                    filter = updatesFilter.ToServerSyncFilter()
                }
            };

            // GetConfig must be false to request updates
            revisionIdRequest.GetRevisionIdList.filter.GetConfig = false;

            var revisionsIdReply = await ServerSyncClient.GetRevisionIdListAsync(revisionIdRequest);
            if (revisionsIdReply?.GetRevisionIdListResponse1?.GetRevisionIdListResult == null)
            {
                throw new Exception("Failed to get revision ID list");
            }

            // Return IDs and the anchor for this query. The anchor can be used to get a delta list in the future.
            return revisionsIdReply.GetRevisionIdListResponse1.GetRevisionIdListResult.NewRevisions.Select(
                rawId => new MicrosoftUpdatePackageIdentity(rawId.UpdateID, rawId.RevisionNumber));
        }

        private async Task RefreshTokenAndConfig()
        {
			if (AccessToken == null || AccessToken.ExpiresIn(TimeSpan.FromMinutes(2)))
			{
				await RefreshAccessToken(null, null);
			}

			// If no configuration is known, query it now
			if (ConfigData == null)
			{
				await RefreshServerConfigData();
			}
		}

        /// <summary>
        /// Retrieves update data for the list of update ids
        /// </summary>
        /// <param name="updateIds">The ids to retrieve data for</param>
        internal async IAsyncEnumerable<MicrosoftUpdatePackage> GetUpdateDataForIds(MicrosoftUpdatePackageIdentity[] updateIds, CancellationToken cancelToken)
        {
			cancelToken.ThrowIfCancellationRequested();

			await RefreshTokenAndConfig();

            var retrieveBatches = updateIds
                .Select(id => new UpdateIdentity() { UpdateID = id.ID, RevisionNumber = id.Revision })
                .Chunk(ConfigData.MaxNumberOfUpdatesPerRequest);

            foreach(var batch in retrieveBatches)
            {
                cancelToken.ThrowIfCancellationRequested();

				var updateDataRequest = new GetUpdateDataRequest
                {
                    GetUpdateData = new GetUpdateDataRequestBody()
                    {
                        cookie = AccessToken.AccessCookie,
                        updateIds = batch
                    }
                };

                var updateDataReply = await ServerSyncClient.GetUpdateDataAsync(updateDataRequest);

                if (updateDataReply?.GetUpdateDataResponse1?.GetUpdateDataResult == null)
                {
					throw new Exception("Failed to get update data");
				}

                // Parse the list of raw files into a more usable format
                var filesList = updateDataReply.GetUpdateDataResponse1.GetUpdateDataResult.fileUrls
                    .Select(rawFile => InMemoryUpdateFactory.FromServerSyncData(rawFile))
                    .ToDictionary(file => file.DigestBase64);

                foreach (var rawUpdate in updateDataReply.GetUpdateDataResponse1.GetUpdateDataResult.updates)
                {
					yield return InMemoryUpdateFactory.FromServerSyncData(rawUpdate, filesList);
                }
            }
        }
    }
}
