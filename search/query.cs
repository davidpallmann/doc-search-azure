using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;

namespace search
{
    public static class query
    {
        private const string EndpointUrl = "https://...your-cosmos-db-endpoint.documents.azure.com:443/";
        private const string PrimaryKey = "...your-cosmos-db-access-key...";

        [FunctionName("query")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            DocumentClient client = new DocumentClient(new Uri(EndpointUrl), PrimaryKey);

            // get the search term

            string search = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "search", true) == 0)
                .Value;

            if (search == null)
            {
                dynamic data = await req.Content.ReadAsAsync<object>();
                search = data?.name;
            }

            // If a quoted term was passed such as "the man", set isQuoted flag. This will add an additional level of search
            // against the extracted document text for an exact match of the text sequence.

            bool isQuoted = false;
            if (search.StartsWith("\"") && search.EndsWith("\"") && search.Length > 2)
            {
                isQuoted = true;
                search = search.Substring(1, search.Length - 2);
            }

            // Query all DocWordCollection records containing the search term - query each word separately

            List<String> docs = new List<String>();

            try
            {
                foreach (String term in search.Split(' '))
                {
                    log.Info("Querying for term: " + term);
                    FeedOptions queryOptions = new FeedOptions { EnableCrossPartitionQuery = true, MaxItemCount = -1 };

                    IQueryable<DocWord> results = client.CreateDocumentQuery<DocWord>(
                       UriFactory.CreateDocumentCollectionUri("SearchDB", "DocWordCollection"),
                       queryOptions)
                       .Where(m => m.Word == term);

                    if (results == null || results.Count() == 0)
                    {
                        log.Info("Query found no records");
                    }
                    else
                    {
                        foreach (DocWord rec in results)
                        {
                            if (!docs.Contains(rec.DocId)) docs.Add(rec.DocId);
                        }
                        log.Info("Query found " + results.Count().ToString() + " records");
                    }
                }
            }
            catch (DocumentClientException de)
            {
                log.Info("ERROR during query: " + de.Message);
            }

            // If search term was in quotes, we must now inspect each potential result match's document record
            // and check whether the search query appears exactly or not in the extracted text.

            if (isQuoted)
            {
                try
                {
                    log.Info("Performing additional query for exact search term");
                    FeedOptions queryOptions = new FeedOptions { EnableCrossPartitionQuery = true, MaxItemCount = -1 };
                    IQueryable<Document> results = null;

                    List<String> potentialMatches = docs;
                    docs = new List<string>();
                    foreach (String docName in potentialMatches)
                    {
                        results = client.CreateDocumentQuery<Document>(
                            UriFactory.CreateDocumentCollectionUri("SearchDB", "DocCollection"),
                            queryOptions)
                            .Where(m => m.DocId == docName);
                        if (results != null)
                        {
                            foreach (Document doc in results)
                            {
                                if (doc != null && doc.Text != null && 
                                    (CultureInfo.InvariantCulture.CompareInfo.IndexOf(doc.Text, search, CompareOptions.IgnoreCase))!=-1)
                                {
                                    if (!docs.Contains(docName)) docs.Add(docName);
                                }
                            }
                        }
                    }
                }
                catch(Exception de)
                {
                    log.Info("ERROR during query-quoted: " + de.Message);
                }
            }

            return search == null
                ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a search= term on the query string or in the request body")
                : req.CreateResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(docs));
        }
    }
}
