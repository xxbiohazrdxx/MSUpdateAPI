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

## License

[MIT](https://choosealicense.com/licenses/mit/)