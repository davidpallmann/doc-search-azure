using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using IFilterTextReader;
using System.Net;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;

namespace search
{
    public static class index
    {
        private const string EndpointUrl = "https://...your-cosmos-db-endpoint.documents.azure.com:443/";
        private const string PrimaryKey = "...your-cosmos-db-access-key...";
        private static DocumentClient client;

        private const bool LogDetail = false;

        [FunctionName("index")]
        public static void Run([BlobTrigger("docs/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string name, ILogger log)
        {
            log.LogInformation($"Blob trigger: Name: {name}, Size: {myBlob.Length} bytes");
            log.LogInformation("---- Start of Job: index " + name);

            String tempPath = Path.Combine(Path.GetTempPath(), name);

            using (System.IO.FileStream output = new System.IO.FileStream(tempPath, FileMode.Create))
            {
                myBlob.CopyTo(output);
            }

            if (LogDetail) log.LogInformation("---- written to disk file " + tempPath + " ----");

            String text = ExtractTextFromFile(log, tempPath, null);
            if (LogDetail) log.LogInformation("extracted text from file");
            log.LogInformation("Text of file extracted");

            AddDocumentRecord(log, name, myBlob.Length, text).Wait();

            log.LogInformation("---- End of Job: index " + name);
        }

        #region ExtractTextFromFile

        // Extract searchable text from a file using IFilterTextReader. 
        // Extract text from document, then replace multiple white space sequences with a single space. 
        // If IFilterTextReader fails (for example, old Office document; or unknown document type), an exception is logged and null is returned.
        // Prefix is optional text to prepend to the result - such as document filename, metadata properties, anything else to include in search text.

        private static String ExtractTextFromFile(ILogger log, String inputFile, String prefix = null)
        {
            String line;
            String cleanedString = prefix;

            try
            {
                FilterReaderOptions options = new FilterReaderOptions() { };

                using (var reader = new FilterReader(inputFile, string.Empty, options))
                {
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (!String.IsNullOrEmpty(line))
                        {
                            line = System.Text.RegularExpressions.Regex.Replace(line, @"[,]\s+", " ");
                            line = System.Text.RegularExpressions.Regex.Replace(line, @"[,]", "");
                            line = System.Text.RegularExpressions.Regex.Replace(line, @"[^a-zA-Z'\d\s:]", " ");
                            line = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ");
                            cleanedString += line + " ";
                        }
                    }
                } // end reader
            }
            catch (Exception ex) 
            {
                log.LogError("ExtractTextFromFile: " + ex.Message);
            }

            return cleanedString;
        }

        #endregion

        #region AddDocumentRecord

        private static async Task AddDocumentRecord(ILogger log, String name, long length, String text)
        {
            try
            {
                // https://nlp.stanford.edu/IR-book/html/htmledition/dropping-common-terms-stop-words-1.html
                List<String> stopWords = new List<string>();
                stopWords.Add("a");
                stopWords.Add("an");
                stopWords.Add("and");
                stopWords.Add("are");
                stopWords.Add("as");
                stopWords.Add("at");
                stopWords.Add("be");
                stopWords.Add("by");
                stopWords.Add("for");
                stopWords.Add("from");
                stopWords.Add("has");
                stopWords.Add("he");
                stopWords.Add("in");
                stopWords.Add("is");
                stopWords.Add("it");
                stopWords.Add("its");
                stopWords.Add("of");
                stopWords.Add("on");
                stopWords.Add("that");
                stopWords.Add("the");
                stopWords.Add("to");
                stopWords.Add("was");
                stopWords.Add("were");
                stopWords.Add("will");
                stopWords.Add("with");

                client = new DocumentClient(new Uri(EndpointUrl), PrimaryKey);

                // Create database and collection if necessary

                await client.CreateDatabaseIfNotExistsAsync(new Database { Id = "SearchDB" });
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("SearchDB"), new DocumentCollection { Id = "DocCollection" }, new RequestOptions { PartitionKey = new PartitionKey("/Category"), OfferThroughput = 400 });
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("SearchDB"), new DocumentCollection { Id = "DocWordCollection" }, new RequestOptions { PartitionKey = new PartitionKey("/DocId"), OfferThroughput = 400 });

                // From extracted text, create a collection of unique words

                String[] items = text.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int wordCount = items.Length;
                IEnumerable<String> uniqueWords = items.Distinct();
                int uniqueWordCount = uniqueWords.Count();

                // Add Document record

                String docType = null;
                int pos = name.LastIndexOf(".");
                if (pos != -1) docType = name.Substring(pos + 1);

                Document document = new Document()
                {
                    Category = "docs",
                    DocId = name, 
                    Name = name,
                    DocType = docType,
                    Owner = "David Pallmann",
                    Size = Convert.ToInt32(length),
                    Text = text,
                    Words = text.Split(' ').Length + 1
                };

                try
                {
                    // Partition key provided either doesn't correspond to definition in the collection or doesn't match partition key field values specified in the document.
                    log.LogInformation("Creating document - Category: " + document.Category + ", DocId: " + document.DocId); // + name);
                    await client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri("SearchDB", "DocCollection"), document, new RequestOptions() { PartitionKey = new PartitionKey("docs") });
                    log.LogInformation("Document record created, Id: " + document.DocId);
                }
                catch (DocumentClientException de)
                {
                    try
                    {
                        log.LogInformation("ERROR creating document: " + de.GetType().Name + ": " + de.Message);

                        // Create document failed, so perform a Replace instead
                        log.LogInformation("Document exists, Replacing existing document");

                        var docCollectionUrl = UriFactory.CreateDocumentCollectionUri("SearchDB", "DocCollection");
                        var docCollection = (await client.ReadDocumentCollectionAsync(docCollectionUrl, new RequestOptions() { PartitionKey = new PartitionKey("docs") })).Resource;
                        var query = new SqlQuerySpec("SELECT * FROM DocCollection doc WHERE doc.DocId = @DocId",
                            new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@DocId", Value = document.DocId } }));
                        var existingDocRecords = client.CreateDocumentQuery<Microsoft.Azure.Documents.Document>(docCollectionUrl, query, new FeedOptions() { EnableCrossPartitionQuery=true }).AsEnumerable();
                        if (existingDocRecords != null && existingDocRecords.Count() > 0)
                        {
                            Microsoft.Azure.Documents.Document doc = existingDocRecords.First<Microsoft.Azure.Documents.Document>();

                            doc.SetPropertyValue("Category", document.Category);
                            doc.SetPropertyValue("DocId", document.DocId);
                            doc.SetPropertyValue("Name", document.Name);
                            doc.SetPropertyValue("DocType", document.DocType);
                            doc.SetPropertyValue("Owner", document.Owner);
                            doc.SetPropertyValue("Text", document.Text);
                            doc.SetPropertyValue("Words", document.Words);

                            await client.ReplaceDocumentAsync(existingDocRecords.First<Microsoft.Azure.Documents.Document>().SelfLink, doc, new RequestOptions() { PartitionKey = new PartitionKey("docs") });
                            log.LogInformation("Document record replaced, Id: " + document.DocId);
                        }
                    }
                    catch (DocumentClientException de2)
                    {
                        log.LogInformation("ERROR replacing document: " + de2.GetType().Name + ": " + de2.Message);
                    }
                }

                var collUrl = UriFactory.CreateDocumentCollectionUri("SearchDB", "DocWordCollection");
                var doc1 = (await client.ReadDocumentCollectionAsync(collUrl, new RequestOptions() { PartitionKey = new PartitionKey(document.DocId) })).Resource;
                var existingDocWordRecords = client.CreateDocumentQuery(doc1.SelfLink, new FeedOptions() { PartitionKey = new PartitionKey(document.DocId) }).AsEnumerable().ToList();

                if (existingDocWordRecords != null)
                {
                    int count = 0;
                    try
                    {
                        RequestOptions options = new RequestOptions() { PartitionKey = new PartitionKey(document.DocId) };
                        log.LogInformation("Deleting prior DocWord records...");
                        foreach (Microsoft.Azure.Documents.Document word in existingDocWordRecords)
                        {
                            if (LogDetail) log.LogInformation("Found document SelfLink: " + word.SelfLink + ", DocId: " + word.GetPropertyValue<String>("DocId") + ", Word: " + word.GetPropertyValue<String>("Word"));
                            await client.DeleteDocumentAsync(word.SelfLink /* UriFactory.CreateDocumentUri("SearchDB", "DocWordCollection", word.SelfLink) */, options); //, options);
                            count++;
                        }
                    }
                    catch (DocumentClientException de)
                    {
                        log.LogInformation("ERROR deleting DocWord record: " + de.Message);
                    }
                    catch (Exception ex)
                    {
                        log.LogInformation("EXCEPTION deleting DocWord record: " + ex.GetType().Name + ": " + ex.Message);
                        if (ex.InnerException != null)
                        {
                            log.LogInformation("INNER EXCEPTION: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
                        }
                    }
                    log.LogInformation(count.ToString() + " DocWord records deleted");
                }

                // Store document words in Words collection

                try
                {
                    log.LogInformation("Adding DocWord records with partition key " + document.DocId + "...");
                    int count = 0;
                    DocWord docWord = null;
                    foreach (String word in uniqueWords)
                    {
                        if (!stopWords.Contains(word))
                        {
                            docWord = new DocWord()
                            {
                                Id = Guid.NewGuid().ToString(),
                                DocId = document.DocId,
                                Word = word
                            };
                            if (LogDetail) log.LogInformation("About to: CreateDocumentAsync on DocWordCollection: word: " + docWord.Word + ", DocId:" + docWord.DocId);
                            await client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri("SearchDB", "DocWordCollection"), docWord, new RequestOptions() { PartitionKey = new PartitionKey(document.DocId) });
                            count++;
                        }
                    }
                    log.LogInformation(count.ToString() + " DocWord records created");
                }
                catch (DocumentClientException de)
                {
                    log.LogInformation("ERROR creating DocWord record: " + de.Message);
                }
            }
            catch (DocumentClientException de)
            {
                Exception baseException = de.GetBaseException();
                log.LogInformation("{0} error occurred: {1}, Message: {2}", de.StatusCode, de.Message, baseException.Message);
            }
            catch (Exception e)
            {
                Exception baseException = e.GetBaseException();
                log.LogInformation("Error: {0}, Message: {1}", e.Message, baseException.Message);
            }
        }

        #endregion
    }
}
