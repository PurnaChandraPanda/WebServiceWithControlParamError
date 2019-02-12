using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Web.Services.Configuration;
using System.Web.Services.Protocols;

namespace WebApplication1
{
    public class CustomSoapProtocolFactory: SoapServerProtocolFactory
    {
        private static bool s_fixedUp = false;
        private static object s_lock = new object();

        public CustomSoapProtocolFactory(): base()
        {
            // This custom SoapProtocolFactory is being used solely to execute FixupReader before the parameterReaderTypes is first read.
            // ProtocolFactory instances are instantiated before the reader types array is read so call FixupReader in the constructor.
            FixupReader();
        }

        public static void FixupReader()
        {
            if (s_fixedUp)
                return;
            lock(s_lock)
            {
                if (s_fixedUp)
                    return;

                var webServiceSectionType = typeof(WebServicesSection);
                var parameterReaderTypesField = webServiceSectionType.GetField("parameterReaderTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                Type[] parameterReaderTypes = (Type[])parameterReaderTypesField.GetValue(WebServicesSection.Current);

                for (int i=0; i < parameterReaderTypes.Length; i++)
                {
                    if (typeof(HtmlFormParameterReader) == parameterReaderTypes[i])
                        parameterReaderTypes[i] = typeof(WrappedHtmlFormParameterReader);
                }

                s_fixedUp = true;
            }
        }
    }

    public class WrappedHtmlFormParameterReader : MimeParameterReader
    {
        // This class is a copy of HtmlFormParameterReader and ValueCollectionParameterReader merged together into a single class.
        // The Read method copied from ValueCollectionParameterReader has been modified to not throw when unable to parse a parameter.
        // Instead, it returns the default value of the parameter type. e.g. default(int) if the parameter is an int.
        internal const string MimeType = "application/x-www-form-urlencoded";

        ParameterInfo[] paramInfos;

        public WrappedHtmlFormParameterReader()
        {
        }

        public override void Initialize(object o)
        {
            paramInfos = (ParameterInfo[])o;
        }

        public override object GetInitializer(LogicalMethodInfo methodInfo)
        {
            if (!ValueCollectionParameterReader.IsSupported(methodInfo)) return null;
            return methodInfo.InParameters;
        }

        public override object[] Read(HttpRequest request)
        {
            if (!MatchesBase(request.ContentType, MimeType)) return null;
            return Read(request.Form);
        }

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

        // FromString, ArrayElementFromString (modified version of FromString specialized for reading an array element),
        // MatchesBase and GetBase are modified versions copied from System.Web.Services.Protocols.ScalarFormatter
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

        internal static object ArrayElementFromString(string value, Type type)
        {
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

                // Array.SetValue(null, index) will set default(type) if an array of value type.
                return null;
            }
        }

        internal static bool MatchesBase(string contentType, string baseContentType)
        {
            return string.Compare(GetBase(contentType), baseContentType, StringComparison.OrdinalIgnoreCase) == 0;
        }

        // this returns the "base" part of the contentType/mimeType, e.g. the "text/xml" part w/o
        // the ; CharSet=isoxxx part that sometimes follows.
        internal static string GetBase(string contentType)
        {
            int semi = contentType.IndexOf(';');
            if (semi >= 0) return contentType.Substring(0, semi);
            return contentType;
        }

        public static object GetDefault(Type type)
        {
            if (type.IsValueType)
            {
                // Activator.CreateInstance will return an value type with all bytes set as 0.
                // This is the equivalent of default(type).
                return Activator.CreateInstance(type);
            }

            // If this is a reference type, then the default value will be null
            return null;
        }
    }
}