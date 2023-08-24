// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.PackageGraph.MicrosoftUpdate.Compression;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Content;
using Microsoft.UpdateServices.WebServices.ServerSync;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Source
{
    abstract class InMemoryUpdateFactory
    {
        internal static MicrosoftUpdatePackage FromServerSyncData(ServerSyncUpdateData serverSyncData, Dictionary<string, UpdateFileUrl> filesCollection)
        {
            byte[] metadata;
            if (!string.IsNullOrEmpty(serverSyncData.XmlUpdateBlob))
            {
                metadata = Encoding.Unicode.GetBytes(serverSyncData.XmlUpdateBlob);
            }
            else
            { 
                // If the plain text blob is not availabe, use the compressed XML blob
                if (serverSyncData.XmlUpdateBlobCompressed == null || serverSyncData.XmlUpdateBlobCompressed.Length == 0)
                {
                    throw new Exception("Missing XmlUpdateBlobCompressed");
                }

                // This call will throw an exception if a decompressor is not available for the current platform.
                metadata = CabinetUtility.RecompressUnicodeData(serverSyncData.XmlUpdateBlobCompressed);
            }

            return MicrosoftUpdatePackage.FromMetadataXml(metadata, filesCollection);
        }

        internal static UpdateFileUrl FromServerSyncData(ServerSyncUrlData urlData)
        {
            return new UpdateFileUrl(Convert.ToBase64String(urlData.FileDigest), urlData.MUUrl, urlData.UssUrl);
        }
    }
}
