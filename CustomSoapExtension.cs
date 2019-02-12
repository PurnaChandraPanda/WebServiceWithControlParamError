using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Services.Protocols;
using System.Xml;

namespace WebApplication1
{
    public class CustomSoapExtension: SoapExtension
    {
        Stream oldStream;
        Stream newStream;

        public override Stream ChainStream(Stream stream)
        {
            oldStream = stream;
            newStream = new MemoryStream();
            return newStream;
        }
        public override object GetInitializer(Type serviceType)
        {
            return null;
        }
        public override object GetInitializer(LogicalMethodInfo methodInfo, SoapExtensionAttribute attribute)
        {
            return null;
        }
        public override void Initialize(object initializer)
        {
        }
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

        private void ModifyResponseStream(SoapMessage message)
        {
            //newStream.Position = 0;
            //WriteOutput(message);
            newStream.Position = 0;
            Copy(newStream, oldStream);
        }

        private void WriteOutput(SoapMessage message)
        {
            string soapString = (message is SoapServerMessage) ? "SoapResponse" : "SoapRequest";
        }

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

        void Copy(Stream from, Stream to)
        {
            TextReader reader = new StreamReader(from);
            TextWriter writer = new StreamWriter(to);
            writer.WriteLine(reader.ReadToEnd());
            writer.Flush();
        }
    }
}