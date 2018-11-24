using System.ComponentModel.DataAnnotations;
using TRex.Metadata;

namespace QueueMessageParserAPIApp.Models
{
    public class ServiceBusMessageWrapper
    {
        [Metadata("Content Data", "Message string.")]
        [Required(AllowEmptyStrings = false)]
        public string ContentData { get; set; }

        [Metadata("Content Type", "Message content type. CRM uses application/json, application/xml or by default, application/msbin1")]
        [Required(AllowEmptyStrings = false)]
        public string ContentType { get; set; }

        [Metadata("Content Encoding", "Optional. Set this to 'Base64' if Content Data is not yet decoded from a Base64 encoded string.")]
        public string ContentEncoding { get; set; }
    }
}
