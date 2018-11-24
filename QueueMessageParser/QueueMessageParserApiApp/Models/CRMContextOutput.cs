using System.Collections.Generic;

namespace QueueMessageParserAPIApp.Models
{
    public class CRMContextOutput
    {
        public string messageName { get; set; }

        public Dictionary<string, object> messageParameters { get; set; }
    }
}