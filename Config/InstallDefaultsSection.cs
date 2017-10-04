using System.Collections.Generic;
using System.Configuration;
using System.Xml;

namespace SBSInstaller.Config
{
    public class InstallDefaultsSection : Dictionary<string, string>, IConfigurationSectionHandler
    {
        public object Create(object parent, object configContext, XmlNode section)
        {
            var nodes = section.SelectNodes("add");
            if (nodes != null)
            {
                for (var i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.Attributes == null) continue;
                    var key = node.Attributes["key"].Value;
                    var value = node.Attributes["value"].Value;
                    Add(key, value);
                }
            }
            return this;
        }
    }
}