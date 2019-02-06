# doc-search-azure
Document Search Engine in Azure Functions and Cosmos DB
doc-search-azure is a document search engine, written in C#/.NET, that utilizes Azure Functions and Cosmos DB.

For an overview of this project, please refer to this blog post:

# Cloud Assets

In order to deploy this solution to the cloud, you will need to define the following:
* Azure blob storage account, with a "docs" container for documents and a "site" container for a web site.
* Azure Function App definition, with an index function (blob trigger) and a query function (HTTP trigger)

The solution files will need updating to refer to YOUR cloud assets.

# Building

To build doc-search-azure, you need:
* Visual Studio 2017
* An Azure account
* A CosmosDB database, with a DocCollection (partitioned on /Category) and a DocWordCollection (partitioned on /DocId)

Before building, update the local.settings.json file with your storage account settings.

Publish the search project to your Azure Function app in the cloud.

# Using

Monitor your Function App log so you can view activity.

Upload a document to the "docs" storage account container. 

The index function should run and index the document. This will create one DocCollection record and multiple DocWord records in the database.

Verify the Cosmos DB DocCollection now contains a record for the newly-uploaded document.

Go the query function in the Azure console and test it by adding a query term named search. Set search to some value that should be in the uploaded document and see if a search returns a document result.

Get the web site configured correctly--it needs to point to YOUR HTTP URL for the query function--and try running it. You will need to allow the storage container domain in the CORS setting for the Azure function.


