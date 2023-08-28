# MSUpdateAPI

MSUpdateAPI is a REST API designed to allow easy programmatic access to Windows Update Services without either:
* Running an entire on-prem WSUS implementation for the UpdateServices Powershell module to function
* Parsing HTML from the Microsoft Update Catalog and all of the pitfalls inherent with that approach

This is accomplished by directly connecting to the Windows Update Services SOAP endpoints, downloading the needed metadata, parsing, and storing it locally for quick access with minimal storage requirements.

The application is designed to run directly in Azure Apps with Cosmos DB, and was developed using the free tier of each. Except for the initial metadata synchronization, it has minimal CPU/memory/bandwidth requirements.

The implementation of the WSUSSS protocol is taken from the [update-server-server-sync](https://github.com/microsoft/update-server-server-sync) repository, but has been heavily modified to add additional needed features and to add async functionality.

## Configuration

### Database

If running in Azure Apps, three environment variables are needed:

```
DatabaseConfiguration__Uri = <the URI of your Cosmos DB endpoint>
DatabaseConfiguration__PrimaryKey = <Cosmos DB access key>
DatabaseConfiguration__DatabaseName = <the name of the database to be used in Cosmos DB>
```

Note that the application will automatically create the database with a manual throughput limit of 1000RU/s at the database level. As such you should not attempt to create the database yourself inside of Cosmos DB.

If running locally, you can use the Cosmos DB emulator and place the above configuration details in the `appsettings.json` file.

### Update Categories and Products

Windows Update Services contain an enormous amount of updates, many of which are not likely to be needed for your particular use case. By limiting the types of updates and the products for which they apply, the time and storage needed for metadata download can be significantly reduced.

By default the `appsettings.json` configuration file has the following update classification enabled:
* Security Updates

And the following products enabled:
* Windows Server 2019
* Microsoft server operating system, version 21H2 (aka Windows Server 2022 😐)
* Windows 10, version 1903 and later
* Windows 11

Additional update categories and products can be enabled by adding the appropriate GUIDs in the `appsettings.json` configuration file.

## Usage

### Status

Easily check the state of the application

#### Request
`GET /api`

#### Response

```json
{
  "state": "Loading Metadata",
  "categoryCount": 10,
  "productCount": 400,
  "updateCount": 801,
  "recentLogs": [
    "Loading update metadata: 150/1246",
    "Loading update metadata: 149/1246",
    "Loading update metadata: 148/1246",
    "Loading update metadata: 147/1246",
    "Loading update metadata: 146/1246",
    "Loading update metadata: 145/1246",
    "Loading update metadata: 144/1246",
    "Loading update metadata: 143/1246",
    "Loading update metadata: 142/1246",
    "Loading update metadata: 141/1246"
  ]
}
```

### Categories

Lists the update categories enabled, or optionally all categories in the database

#### Request
`GET /api/category`

or

`GET /api/category?ShowDisabled=true` 

#### Response
```json
[
  {
    "id": "0fa1201d-4330-4fa8-8ae9-b877473b6441",
    "name": "Security Updates"
  }
]
```

### Products

Lists the products enabled, or optionally all products in the database. Note that products are represented in a tree like structure with the root node always being `Microsoft`. The root node contains groupings for entire product lines, e.g. `Windows`.

#### Request
`GET /api/product`

or

`GET /api/product?ShowDisabled=true` 

#### Response
```json
{
  "id": "56309036-4c77-4dd9-951a-99ee9c246a94",
  "name": "Microsoft",
  "subproducts": [
    {
      "id": "6964aab4-c5b5-43bd-a17d-ffb4346a8e1d",
      "name": "Windows",
      "subproducts": [
        {
          "id": "72e7624a-5b00-45d2-b92f-e561c0a6a160",
          "name": "Windows 11",
          "subproducts": []
        },
        {
          "id": "2c7888b6-f9e9-4ee9-87af-a77705193893",
          "name": "Microsoft Server Operating System-22H2",
          "subproducts": []
        },
        {
          "id": "b3c75dc1-155f-4be4-b015-3f1a91758e52",
          "name": "Windows 10, version 1903 and later",
          "subproducts": []
        },
        {
          "id": "f702a48c-919b-45d6-9aef-ca4248d50397",
          "name": "Windows Server 2019",
          "subproducts": []
        }
      ]
    }
  ]
}
```

### Updates

Lists the updates that match the enabled categories and products.

#### Request
`GET /api/update`

#### Response
```json
[
  {
    "id": "36a4b6e1-3c98-4d7a-85d6-b92e434a6990",
    "title": "2023-08 Cumulative Update for Microsoft server operating system, version 22H2 for x64-based Systems (KB5029250)",
    "description": "Install this update to resolve issues in Windows. For a complete listing of the issues that are included in this update, see the associated Microsoft Knowledge Base article for more information. After you install this item, you may have to restart your computer.",
    "creationDate": "2023-08-08T13:00:08-04:00",
    "kbArticleId": "5029250",
    "products": [
      {
        "id": "2c7888b6-f9e9-4ee9-87af-a77705193893",
        "name": "Microsoft Server Operating System-22H2"
      }
    ],
    "classification": {
      "id": "0fa1201d-4330-4fa8-8ae9-b877473b6441",
      "name": "Security Updates"
    },
    "files": [
      {
        "fileName": "Windows10.0-KB5029250-x64.cab",
        "source": "http://download.windowsupdate.com/d/msdownload/update/software/secu/2023/08/windows10.0-kb5029250-x64_bf3df74805686dd84faf8983aa55de2a5074bae3.cab",
        "modifiedDate": "2023-08-04T12:00:58-04:00",
        "digest": {
          "algorithm": "SHA1",
          "value": "BF3DF74805686DD84FAF8983AA55DE2A5074BAE3"
        },
        "size": 344979156
      }
    ]
  },
...
]
```

## License

[MIT](https://choosealicense.com/licenses/mit/)