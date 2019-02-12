# WebServiceWithControlParamError
This sample would help demonstrate the methodologies for control parameter format error for request payload in web service.


My customer was willing to control the FormatException error message in case of legacy web service application (ASMX).BY design, SOAP web service gives us 3 methods:
1. HTTP POST
2. POST SOAP 1.1
3. POST SOAP 1.2

Out here, two types of flows to consider:
1. When request is being hit from browser client, it follows the pipeline of *[HttpServerProtocol.ReadParameters](https://referencesource.microsoft.com/#System.Web.Services/System/Web/Services/Protocols/HttpServerProtocol.cs,202bce399bbc50ae)*, where it just reads the **HttpContext** object and performs the parsing activity for various xml nodes in the soap body.
2. Similarly, when the request is being hit from POSTMAN or fiddler like client where they can feed in the soap envelope request, it follows the code pipeline of *[SoapServerProtocol.ReadParameters](https://referencesource.microsoft.com/#System.Web.Services/System/Web/Services/Protocols/SoapServerProtocol.cs,9b7fdbb6afeee992)*, where it tries to perform the “DeSerialize” activity for payload w.r.t. .NET schema, and it finds an incompatible schema and just breaks.

### Failing when hit from browser
```
System.ArgumentException: Cannot convert 2q to System.Int32.
Parameter name: type ---> System.FormatException: Input string was not in a correct format.
   at System.Number.StringToNumber(String str, NumberStyles options, NumberBuffer& number, NumberFormatInfo info, Boolean parseDecimal)
   at System.Number.ParseInt32(String s, NumberStyles style, NumberFormatInfo info)
   at System.String.System.IConvertible.ToInt32(IFormatProvider provider)
   at System.Convert.ChangeType(Object value, Type conversionType, IFormatProvider provider)
   at System.Web.Services.Protocols.ScalarFormatter.FromString(String value, Type type)
   --- End of inner exception stack trace ---
   at System.Web.Services.Protocols.ScalarFormatter.FromString(String value, Type type)
   at System.Web.Services.Protocols.ValueCollectionParameterReader.Read(NameValueCollection collection)
   at System.Web.Services.Protocols.HtmlFormParameterReader.Read(HttpRequest request)
   at System.Web.Services.Protocols.HttpServerProtocol.ReadParameters()
   at System.Web.Services.Protocols.WebServiceHandler.CoreProcessRequest()
```

### Failing when hit from POSTMAN
```
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
    <soap:Body>
        <soap:Fault>
            <soap:Code>
                <soap:Value>soap:Sender</soap:Value>
            </soap:Code>
            <soap:Reason>
                <soap:Text xml:lang="en">System.Web.Services.Protocols.SoapException: Server was unable to read request. ---&gt; System.InvalidOperationException: There is an error in XML document (4, 25). ---&gt; System.FormatException: Input string was not in a correct format.
   at System.Number.StringToNumber(String str, NumberStyles options, NumberBuffer&amp; number, NumberFormatInfo info, Boolean parseDecimal)
   at System.Number.ParseInt32(String s, NumberStyles style, NumberFormatInfo info)
   at Microsoft.Xml.Serialization.GeneratedAssembly.XmlSerializationReader1.Read1_HelloWorld()
   at Microsoft.Xml.Serialization.GeneratedAssembly.ArrayOfObjectSerializer.Deserialize(XmlSerializationReader reader)
   at System.Xml.Serialization.XmlSerializer.Deserialize(XmlReader xmlReader, String encodingStyle, XmlDeserializationEvents events)
   --- End of inner exception stack trace ---
   at System.Xml.Serialization.XmlSerializer.Deserialize(XmlReader xmlReader, String encodingStyle, XmlDeserializationEvents events)
   at System.Web.Services.Protocols.SoapServerProtocol.ReadParameters()
   --- End of inner exception stack trace ---
   at System.Web.Services.Protocols.SoapServerProtocol.ReadParameters()
   at System.Web.Services.Protocols.WebServiceHandler.CoreProcessRequest()</soap:Text>
            </soap:Reason>
            <soap:Detail />
        </soap:Fault>
    </soap:Body>
</soap:Envelope>
```

## Solution Approach
Tried out the **SoapExtension** approach, and we could say it works "only" if the client is "postman"” or "".NET app", where input request format is actually SOAP, but not Http POST. Challenge out here was when request being hit from browser (i.e. our .asmx page method test), it never reaches the "SoapExtension". 

As per the web service code path, behavioral difference is in **SoapServerProtocol** vs **HttpServerProtocol** "ReadParameters" API. For the case of .NET or Postman client, it follows the pipeline of SoapServerProtocol. Whereas for the case of asmx page browser client, it follows the pipeline of HttpServerProtocol.

By capturing Fiddler traces, i.e. while invoking the "WebMethod", it turns out they are focused on HTTP POST flow, where content-type is **application/x-www-form-urlencoded**. 

### 1 - SoapExtension logic to override SoapMessage in incoming [in CustomSoapExtension.cs]

```
        public override void ProcessMessage(SoapMessage message)
        {
            switch (message.Stage)
            {
                case SoapMessageStage.AfterDeserialize:
                    break;
                case SoapMessageStage.AfterSerialize:
                    break;
                case SoapMessageStage.BeforeDeserialize:
                    ModifyRequestStream(message);
                    break;
                case SoapMessageStage.BeforeSerialize:
                    break;
            }
        }
```

```
        private void ModifyRequestStream(SoapMessage message)
        {
            Copy(oldStream, newStream);
            var doc = WriteInput(message);
            newStream.Position = 0;
            doc.Save(newStream);
            newStream.Position = 0;
        }

        private XmlDocument WriteInput(SoapMessage message)
        {
            string soapString = (message is SoapServerMessage) ? "SoapRequest" : "SoapResponse";
            XmlDocument doc = new XmlDocument();
            XmlWriter newWriter = XmlWriter.Create(newStream);
            newWriter.Flush();
            newStream.Position = 0;
            doc.Load(newStream);

            // validation logic for xml nodes' values
            try
            {
                var inputValue = doc.GetElementsByTagName("number").Item(0).FirstChild.OuterXml;
                Int32.Parse(inputValue);
            }
            catch (Exception e) {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException)
                {
                    throw;
                }
                else if (e is FormatException)
                {
                    // option 1: throw exception with suitable message
                    //throw new Exception("custom format exception message");

                    // option 2: modify the input to a default value and move on
                    doc.GetElementsByTagName("number").Item(0).InnerText = "0"; 
                }
            }

            string xml = doc.OuterXml;
            doc.LoadXml(xml);
            return doc;
        }
```

### 2 - Custom SoapServerProtocolFactory [in CustomSoapProtocolFactory.cs]
HttpPostServerProtocolFactory is in the ServerProtocolFactories array immediately after SoapServerProtocolFactory so there’s no risk of skipping any other factories between them. Hence, the trick is to create a new ServerProtocolFactory which combines the two factory implementations and can return a SoapServerProtocol instance of an HttpPostServerProtocol instance (needed to be constructed using reflection or Activator.CreateInstance). When creating the HttpPostServerProtocol instance, wrap it in a delegating ServerProtocol instance which puts a try/catch around the throwing method and handles it.

Basically, WebServiceSection class have [parameterReaderTypes](https://referencesource.microsoft.com/#System.Web.Services/System/Web/Services/Configuration/WebServicesSection.cs,d5c9cca83a2cbec7,references) internal property that controls type of paraemeter reader to consider. In the runtime, we are going to replace **HtmlFormParameterReader** with custom **MimeParameterReader**. This is for **Http POST**.

#### Hooking custom parameter reader
```
                var webServiceSectionType = typeof(WebServicesSection);
                var parameterReaderTypesField = webServiceSectionType.GetField("parameterReaderTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                Type[] parameterReaderTypes = (Type[])parameterReaderTypesField.GetValue(WebServicesSection.Current);

                for (int i=0; i < parameterReaderTypes.Length; i++)
                {
                    if (typeof(HtmlFormParameterReader) == parameterReaderTypes[i])
                        parameterReaderTypes[i] = typeof(WrappedHtmlFormParameterReader);
                }
```

#### Read the paramter values from NameValueCollection
```
 protected object[] Read(NameValueCollection collection)
        {
            object[] parameters = new object[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                ParameterInfo paramInfo = paramInfos[i];
                if (paramInfo.ParameterType.IsArray)
                {
                    string[] arrayValues = collection.GetValues(paramInfo.Name);
                    Type arrayType = paramInfo.ParameterType.GetElementType();
                    Array array = Array.CreateInstance(arrayType, arrayValues.Length);
                    for (int j = 0; j < arrayValues.Length; j++)
                    {
                        string value = arrayValues[j];
                        array.SetValue(ArrayElementFromString(value, arrayType), j);
                    }
                    parameters[i] = array;
                }
                else
                {
                    string value = collection[paramInfo.Name];
                    if (value == null) throw new InvalidOperationException($"WebMissingParameter {paramInfo.Name}");
                    parameters[i] = FromString(value, paramInfo);
                }
            }
            return parameters;
        }

        internal static object FromString(string value, ParameterInfo paramInfo)
        {
            var type = paramInfo.ParameterType;
            try
            {
                if (type == typeof(string))
                    return value;
                else if (type.IsEnum)
                    return Enum.Parse(type, value);
                else
                    return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException)
                {
                    throw;
                }

                // If we get an exception when trying to parse the value, get the type default value
                return GetDefault(type);
            }
        }
```


Note: We have both SoapExtension and SoapServerProtocolFactory classes configured to control the runtime, i.e. SOAP and HTTP post flows respectively.
