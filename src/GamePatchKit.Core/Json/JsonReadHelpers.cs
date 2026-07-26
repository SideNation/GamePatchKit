using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Json
{
    // Small shared primitives used by every model's TryParse method to read Newtonsoft JObject/JArray input
    // and report layer-1 (schema-shape) problems uniformly. Semantic (layer-2) rules live in each model's
    // own Validator instead.
    public static class JsonReadHelpers
    {
        public static IReadOnlyList<string> FindUnknownProperties(JObject obj, ISet<string> knownNames)
        {
            var unknown = new List<string>();

            foreach (JProperty property in obj.Properties())
            {
                if (!knownNames.Contains(property.Name))
                {
                    unknown.Add(property.Name);
                }
            }

            return unknown;
        }

        public static bool TryGetRequiredString(JObject obj, string propertyName, out string value)
        {
            if (obj.TryGetValue(propertyName, out JToken? token) && token!.Type == JTokenType.String)
            {
                value = (string)token!;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public static bool TryGetRequiredInteger(JObject obj, string propertyName, out long value)
        {
            value = 0;
            return obj.TryGetValue(propertyName, out JToken? token) && JsonNumbers.TryGetSafeInteger(token!, out value);
        }

        public static bool TryGetRequiredBoolean(JObject obj, string propertyName, out bool value)
        {
            if (obj.TryGetValue(propertyName, out JToken? token) && token!.Type == JTokenType.Boolean)
            {
                value = (bool)token!;
                return true;
            }

            value = false;
            return false;
        }

        public static bool TryGetRequiredArray(JObject obj, string propertyName, out JArray value)
        {
            if (obj.TryGetValue(propertyName, out JToken? token) && token!.Type == JTokenType.Array)
            {
                value = (JArray)token!;
                return true;
            }

            value = new JArray();
            return false;
        }

        public static bool TryGetRequiredObject(JObject obj, string propertyName, out JObject value)
        {
            if (obj.TryGetValue(propertyName, out JToken? token) && token!.Type == JTokenType.Object)
            {
                value = (JObject)token!;
                return true;
            }

            value = new JObject();
            return false;
        }

        public static bool TryGetOptionalArray(JObject obj, string propertyName, out JArray value, out bool wasPresent)
        {
            if (!obj.TryGetValue(propertyName, out JToken? token))
            {
                value = new JArray();
                wasPresent = false;
                return true;
            }

            wasPresent = true;

            if (token!.Type != JTokenType.Array)
            {
                value = new JArray();
                return false;
            }

            value = (JArray)token!;
            return true;
        }

        public static bool TryGetOptionalObject(JObject obj, string propertyName, out JObject? value)
        {
            if (!obj.TryGetValue(propertyName, out JToken? token))
            {
                value = null;
                return true;
            }

            if (token!.Type != JTokenType.Object)
            {
                value = null;
                return false;
            }

            value = (JObject)token!;
            return true;
        }
    }
}
