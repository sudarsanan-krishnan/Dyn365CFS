namespace QueueMessageParserAPIApp.Controllers
{
    using Microsoft.Xrm.Sdk;
    using Newtonsoft.Json.Linq;
    using Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Text;
    using System.Web.Http;
    using System.Xml;
    using TRex.Metadata;

    /// <summary>
    /// ApiController for deserializing service bus message from CRM into a RemoteExecutionContext.
    /// Extracts and returns the key value pairs in InputParameters of the RemoteExecutionContext.
    /// </summary>
    public class QueueMessageParserController : ApiController
    {
        #region Action - Parse message
        [Metadata("Get CRM message")]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(HttpStatusCode.BadRequest, "An exception occured", typeof(Exception))]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(System.Net.HttpStatusCode.Created)]
        [HttpPost, Route("ParseMessage")]
        public HttpResponseMessage ParseMessage([FromBody]ServiceBusMessageWrapper input)
        {
            try
            {
                RemoteExecutionContext context;
                bool isBase64Encoded = input.ContentEncoding.Equals("Base64", StringComparison.OrdinalIgnoreCase);

                switch (input.ContentType)
                {
                    case "application/json":
                        context = GetCRMContextWithDCJS(input.ContentData, isBase64Encoded);
                        break;

                    case "application/xml":
                        context = GetCRMContextWithDCS(input.ContentData, isBase64Encoded);
                        break;

                    case "application/msbin1":
                    default:
                        context = GetCRMContextWithDCS(input.ContentData, isBase64Encoded, true);
                        break;
                }

                Dictionary<string, object> messageParameters = ExtractMessageParameters(context);

                CRMContextOutput output = new CRMContextOutput
                {
                    messageName = context.MessageName,
                    messageParameters = messageParameters
                };

                return Request.CreateResponse(System.Net.HttpStatusCode.Created, output);
            }
            catch (NullReferenceException ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, @"The input Received by the API was null. This sometimes happens if the message in the Logic App is malformed. Check the message to make sure there are no escape characters like '\'.", ex);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        private static Dictionary<string, object> ExtractMessageParameters(RemoteExecutionContext context)
        {
            Dictionary<string, object> messageParameters = context.InputParameters.ToDictionary(x => x.Key, x => x.Value);
            Dictionary<string, Entity> preImage = context.PreEntityImages != null && context.PreEntityImages.Count > 0 ? context.PreEntityImages.ToDictionary(x => x.Key, x => x.Value) : null;

            if (messageParameters.ContainsKey("Target"))
            {
                Entity crmEntity = null;

                if (messageParameters["Target"] is Entity)
                {
                    crmEntity = messageParameters["Target"] as Entity;
                }
                else if (messageParameters["Target"] is EntityReference)
                {
                    var crmEntityRef = messageParameters["Target"] as EntityReference;
                    crmEntity = new Entity(crmEntityRef.LogicalName) { Id = crmEntityRef.Id };
                }

                if (crmEntity != null)
                {
                    var target = new Dictionary<string, object>();

                    target.Add("id", crmEntity.Id);
                    target.Add("logicalName", crmEntity.LogicalName);

                    if (crmEntity.Attributes != null)
                    {
                        target.Add("attributes", crmEntity.Attributes.ToDictionary(x => x.Key, x => x.Value));
                    }
                    // pulls only the first entity image's attributes - must be modified for multiple preImage entity use
                    if (preImage != null && preImage.Values != null && preImage.Values.First() is Entity){ 
                        target.Add("preImageAttributes", preImage.Values.First().Attributes.ToDictionary(x => x.Key, x => x.Value));
                    }
                    messageParameters = target;
                }
            }

            return messageParameters;
        }

        /// <summary>
        /// Gets the <c>RemoteExecutionContext</c> from a json message from CRM serialized using <c>DataContractJsonSerializer</c>.
        /// </summary>
        /// <param name="message">CRM message</param>
        /// <returns>A <c>RemoteExecutionContext</c> object deserialized from the CRM message.</returns>
        private static RemoteExecutionContext GetCRMContextWithDCJS(string message, bool isBase64Encoded)
        {
            XmlObjectSerializer serializer = new DataContractJsonSerializer(typeof(RemoteExecutionContext));
            return DeserializeCRMContext(message, serializer, isBase64Encoded);
        }

        /// <summary>
        /// Gets the <c>RemoteExecutionContext</c> from an xml message from CRM serialized using <c>DataContractSerializer</c>.
        /// </summary>
        /// <param name="message">CRM message</param>
        /// <returns>A <c>RemoteExecutionContext</c> object deserialized from the CRM message.</returns>
        private static RemoteExecutionContext GetCRMContextWithDCS(string message, bool isBase64Encoded, bool isBinaryFormat = false)
        {
            XmlObjectSerializer serializer = new DataContractSerializer(typeof(RemoteExecutionContext));
            return DeserializeCRMContext(message, serializer, isBase64Encoded, isBinaryFormat);
        }
        
        private static RemoteExecutionContext DeserializeCRMContext(string message, XmlObjectSerializer serializer, bool isBase64Encoded = false, bool isBinary = false)
        {
            byte[] messageBytes = isBase64Encoded ? Convert.FromBase64String(message) : Encoding.UTF8.GetBytes(message);

            using (MemoryStream ms = new MemoryStream(messageBytes, 0, messageBytes.Length))
            {
                if (isBinary)
                {
                    XmlDictionaryReader reader = XmlDictionaryReader.CreateBinaryReader(ms, XmlDictionaryReaderQuotas.Max);
                    return (RemoteExecutionContext)serializer.ReadObject(reader);
                }
                else
                {
                    return (RemoteExecutionContext)serializer.ReadObject(ms);
                }
            }
        }

        #endregion

        #region Action - Parse AMQP Message
        [Metadata("Parse a message queued with AMQP")]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(HttpStatusCode.BadRequest, "An exception occured", typeof(Exception))]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(System.Net.HttpStatusCode.Created)]
        [HttpPost, Route("ParseAMQPMessage")]
        public HttpResponseMessage ParseAMQPMessage([FromBody]ServiceBusMessageWrapper input)
        {
            try
            {
                bool isBase64Encoded = input.ContentEncoding.Equals("Base64", StringComparison.OrdinalIgnoreCase);

                byte[] messageBytes = isBase64Encoded ? Convert.FromBase64String(input.ContentData) : Encoding.UTF8.GetBytes(input.ContentData);
                string decodedString = Encoding.UTF8.GetString(messageBytes);
                // Decoded string may contain certain unwanted characters when the message was queued using AMQP. Strip them.
                decodedString = new string(decodedString.Where(c => !char.IsControl(c)).ToArray());

                int startJsonIndex = decodedString.IndexOf('{');
                int startArrayIndex = decodedString.IndexOf('[');
                int endJsonIndex;

                // Find if it starts with '[' (meaning an array) or '{' (meaning a json object).
                if (startArrayIndex >= 0 && startArrayIndex < startJsonIndex)
                {
                    // The message contains a JArray.
                    startJsonIndex = startArrayIndex;
                    endJsonIndex = decodedString.LastIndexOf(']');
                }
                else
                {
                    // The message is a JObject.
                    endJsonIndex = decodedString.LastIndexOf('}');
                }

                string jsonString = decodedString.Substring(startJsonIndex, endJsonIndex - startJsonIndex + 1);
                var output = new JArray(JToken.Parse(jsonString));

                return Request.CreateResponse(HttpStatusCode.Created, output);
            }
            catch (NullReferenceException ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, @"The input Received by the API was null. This sometimes happens if the message in the Logic App is malformed. Check the message to make sure there are no escape characters like '\'.", ex);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }
        #endregion
    }
}
