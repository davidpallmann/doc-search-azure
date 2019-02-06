using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace search
{
    // DocWord - a word indexed to a document

    public class DocWord
    {
        [JsonProperty(PropertyName = "id")]
        public String Id { get; set; }                  // unique Id (GUID)
        [JsonProperty(PropertyName = "partitionkey")]
        public String DocId { get; set; }               // document Id (filename)
        public String Word { get; set; }                // word

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this.Id);
        }
    }

}
