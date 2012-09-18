﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.IO;
using Simple.NExtLib;
using Simple.NExtLib.IO;
using Simple.Data.OData.Edm;

namespace Simple.Data.OData
{
    public static class DataServicesHelper
    {
        public static IEnumerable<IDictionary<string, object>> GetData(Stream stream, bool scalarResult = false)
        {
            var text = QuickIO.StreamToString(stream);
            if (scalarResult)
                return new[] { new Dictionary<string, object>() { { "$result", text } } };
            else
                return GetData(text);
        }

        public static IEnumerable<IDictionary<string, object>> GetData(Stream stream, out int totalCount)
        {
            var text = QuickIO.StreamToString(stream);
            return GetData(text, out totalCount);
        }

        public static EdmSchema GetSchema(Stream stream)
        {
            return GetSchema(QuickIO.StreamToString(stream));
        }

        public static IEnumerable<IDictionary<string, object>> GetData(string text)
        {
            var feed = XElement.Parse(text);
            return GetData(feed);
        }

        public static IEnumerable<IDictionary<string, object>> GetData(string text, out int totalCount)
        {
            var feed = XElement.Parse(text);
            totalCount = GetDataCount(feed);
            return GetData(feed);
        }

        public static EdmSchema GetSchema(string text)
        {
            var feed = XElement.Parse(text);
            return ParseSchema(feed);
        }

        private static IEnumerable<IDictionary<string, object>> GetData(XElement feed)
        {
            bool mediaStream = feed.Element(null, "entry") != null &&
                               feed.Element(null, "entry").Descendants(null, "link").Attributes("rel").Any(
                                   x => x.Value == "edit-media");

            var entryElements = feed.Name.LocalName == "feed"
                              ? feed.Elements(null, "entry")
                              : new[] { feed };

            foreach (var entry in entryElements)
            {
                var entryData = new Dictionary<string, object>();

                var linkElements = entry.Elements(null, "link").Where(x => x.Descendants("m", "inline").Any());
                foreach (var linkElement in linkElements)
                {
                    var linkData = GetLinks(linkElement);
                    entryData.Add(linkElement.Attribute("title").Value, linkData);
                }

                var entityElement = mediaStream ? entry : entry.Element(null, "content");
                var properties = GetProperties(entityElement).ToIDictionary();
                properties.ToList().ForEach(x => entryData.Add(x.Key, x.Value));

                yield return entryData;
            }
        }

        private static int GetDataCount(XElement feed)
        {
            var count = feed.Elements("m", "count").SingleOrDefault();
            return count == null ? 0 : Convert.ToInt32(count.Value);
        }

        private static IEnumerable<KeyValuePair<string, object>> GetProperties(XElement element)
        {
            if (element == null) throw new ArgumentNullException("element");

            var properties = element.Element("m", "properties");

            if (properties == null) yield break;

            foreach (var property in properties.Elements())
            {
                yield return EdmHelper.Read(property);
            }
        }

        private static object GetLinks(XElement element)
        {
            var feed = element.Element("m", "inline").Elements().SingleOrDefault();
            if (feed == null)
                return null;

            var linkData = GetData(feed);
            return feed.Name.LocalName == "feed" ? (object)linkData : linkData.Single();
        }

        private static EdmSchema ParseSchema(XElement element)
        {
            var schemaRoot = element.Descendants(null, "Schema");

            var typesNamespace = schemaRoot
                .Where(x => x.Descendants(null, "EntityType").Any()).FirstOrDefault().Attribute("Namespace").Value;
            var containersNamespace = schemaRoot
                .Where(x => x.Descendants(null, "EntityContainer").Any()).FirstOrDefault().Attribute("Namespace").Value;

            var complexTypes = ParseComplexTypes(new EdmComplexType[] { },
                schemaRoot.SelectMany(x => x.Descendants(null, "ComplexType")));
            var entityTypes = ParseEntityTypes(complexTypes,
                schemaRoot.SelectMany(x => x.Descendants(null, "EntityType")));
            var associations = ParseAssociations(complexTypes,
                schemaRoot.SelectMany(x => x.Descendants(null, "Association")));
            var entityContainers = ParseEntityContainers(complexTypes,
                schemaRoot.SelectMany(x => x.Descendants(null, "EntityContainer")));

            return new EdmSchema(typesNamespace, containersNamespace, entityTypes, complexTypes, associations, entityContainers);
        }

        private static IEnumerable<EdmComplexType> ParseComplexTypes(IEnumerable<EdmComplexType> complexTypes, IEnumerable<XElement> elements)
        {
            return from e in elements
                   select new EdmComplexType()
                   {
                       Name = e.Attribute("Name").Value,
                       Properties = (from p in e.Descendants(null, "Property")
                                     select ParseProperty(p, complexTypes)).ToArray(),
                   };
        }

        private static IEnumerable<EdmEntityType> ParseEntityTypes(IEnumerable<EdmComplexType> complexTypes, IEnumerable<XElement> elements)
        {
            return from e in elements
                   select new EdmEntityType()
                              {
                                  Name = e.Attribute("Name").Value,
                                  Properties = (from p in e.Descendants(null, "Property")
                                                select ParseProperty(p, complexTypes)).ToArray(),
                                  Key = (from k in e.Descendants(null, "Key")
                                         select ParseKey(k)).Single(),
                              };
        }

        private static IEnumerable<EdmAssociation> ParseAssociations(IEnumerable<EdmComplexType> complexTypes, IEnumerable<XElement> elements)
        {
            return from e in elements
                   select new EdmAssociation()
                              {
                                  Name = e.Attribute("Name").Value,
                                  End = (from p in e.Descendants(null, "End")
                                         select new EdmAssociationEnd()
                                            {
                                                Role = p.Attribute("Role").Value,
                                                Type = p.Attribute("Type").Value,
                                                Multiplicity = p.Attribute("Multiplicity").Value,
                                            }).ToArray(),
                                  ReferentialConstraint = (from c in e.Descendants(null, "ReferentialConstraint")
                                                           select new EdmReferentialConstraint()
                                                               {
                                                                   Principal = (from r in c.Descendants(null, "Principal")
                                                                                select new EdmReferentialConstraintEnd()
                                                                                    {
                                                                                        Role = r.Attribute("Role").Value,
                                                                                        Properties = (from p in r.Descendants(null, "PropertyRef")
                                                                                                      select p.Attribute("Name").Value).ToArray(),
                                                                                    }
                                                                       ).Single(),
                                                                   Dependent = (from r in c.Descendants(null, "Dependent")
                                                                                select new EdmReferentialConstraintEnd()
                                                                                    {
                                                                                        Role = r.Attribute("Role").Value,
                                                                                        Properties = (from p in r.Descendants(null, "PropertyRef")
                                                                                                      select p.Attribute("Name").Value).ToArray(),
                                                                                    }
                                                                       ).Single(),
                                                               }).SingleOrDefault(),
                              };
        }

        private static IEnumerable<EdmEntityContainer> ParseEntityContainers(IEnumerable<EdmComplexType> complexTypes, IEnumerable<XElement> elements)
        {
            return from e in elements
                   select new EdmEntityContainer()
                              {
                                  Name = e.Attribute("Name").Value,
                                  IsDefaulEntityContainer = ParseBooleanAttribute(e.Attribute("m", "IsDefaultEntityContainer")),
                                  EntitySets = (from s in e.Descendants(null, "EntitySet")
                                                select new EdmEntitySet()
                                                    {
                                                        Name = s.Attribute("Name").Value,
                                                        EntityType = s.Attribute("EntityType").Value,
                                                    }).ToArray(),
                                  AssociationSets = (from s in e.Descendants(null, "AssociationSet")
                                                     select new EdmAssociationSet()
                                                         {
                                                             Name = s.Attribute("Name").Value,
                                                             Association = s.Attribute("Association").Value,
                                                             End = (from n in s.Descendants(null, "End")
                                                                    select new EdmAssociationSetEnd()
                                                                        {
                                                                            Role = n.Attribute("Role").Value,
                                                                            EntitySet = n.Attribute("EntitySet").Value,
                                                                        }).ToArray(),
                                                         }).ToArray(),
                                  FunctionImports = (from s in e.Descendants(null, "FunctionImport")
                                                     select new EdmFunctionImport()
                                                     {
                                                         Name = s.Attribute("Name").Value,
                                                         HttpMethod = ParseStringAttribute(e.Attribute("m", "HttpMethod")),
                                                         ReturnType = ParseStringAttribute(e.Attribute(null, "ReturnType")),
                                                         EntitySet = ParseStringAttribute(e.Attribute(null, "EntitySet")),
                                                         Parameters = (from p in s.Descendants(null, "Parameter")
                                                                       select new EdmParameter()
                                                                       {
                                                                           Name = p.Attribute("Name").Value,
                                                                           Type = EdmPropertyType.Parse(p.Attribute("Type").Value, complexTypes),
                                                                       }).ToArray(),
                                                     }).ToArray(),
                              };

        }

        private static EdmProperty ParseProperty(XElement element, IEnumerable<EdmComplexType> complexTypes)
        {
            return new EdmProperty
                       {
                           Name = element.Attribute("Name").Value,
                           Type = EdmPropertyType.Parse(element.Attribute("Type").Value, complexTypes),
                           Nullable = ParseBooleanAttribute(element.Attribute("Nullable")),
                       };
        }

        private static EdmKey ParseKey(XElement element)
        {
            return new EdmKey()
                       {
                           Properties = (from p in element.Descendants(null, "PropertyRef")
                                         select p.Attribute("Name").Value).ToArray()
                       };
        }

        private static bool ParseBooleanAttribute(XAttribute attribute)
        {
            bool result = false;
            if (attribute != null)
            {
                bool.TryParse(attribute.Value, out result);
            }
            return result;
        }

        private static string ParseStringAttribute(XAttribute attribute)
        {
            return attribute == null ? null : attribute.Value;
        }

        public static XElement CreateDataElement(IDictionary<string, object> row)
        {
            var entry = CreateEmptyEntryWithNamespaces();

            var properties = entry.Element(null, "content").Element("m", "properties");

            foreach (var prop in row)
            {
                EdmHelper.Write(properties, prop);
            }

            return entry;
        }

        private static XElement CreateEmptyEntryWithNamespaces()
        {
            var entry = XElement.Parse(Properties.Resources.DataServicesAtomEntryXml);
            entry.Element(null, "updated").SetValue(DateTime.UtcNow.ToIso8601String());
            return entry;
        }

        public static void AddDataLink(XElement container, string associationName, string linkedEntityName, object[] linkedEntityKeyValues)
        {
            var entry = new XElement(container.GetDefaultNamespace() + "link");
            entry.SetAttributeValue("rel", string.Format("http://schemas.microsoft.com/ado/2007/08/dataservices/related/{0}", associationName));
            entry.SetAttributeValue("type", "application/atom+xml;type=Entry");
            entry.SetAttributeValue("title", associationName);
            entry.SetAttributeValue("href", string.Format("{0}({1})", 
                linkedEntityName,
                string.Join(",", linkedEntityKeyValues.Select(x => ExpressionFormatter.FormatValue(x)))));
            container.Add(entry);
        }
    }
}
