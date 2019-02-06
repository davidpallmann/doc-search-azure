using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace search
{
    // Document - master record for an indexed document

    public class Document
    {
        [JsonProperty(PropertyName = "partitionkey")]
        public String Category { get; set; }            // document category
        [JsonProperty(PropertyName = "id")]
        public String DocId { get; set; }                  // document Id
        public String Name { get; set; }                // document name
        public String DocType { get; set; }             // document file type
        public int Size { get; set; }                   // size of document in bytes
        public String Owner { get; set; }               // name of owner user
        public int Words { get; set; }                  // word count
        public String Text { get; set; }                // searchable text of document
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this.Name);
        }
    }

}
