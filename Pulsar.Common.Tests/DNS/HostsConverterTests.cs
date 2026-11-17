using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.DNS;
using System.Collections.Generic;

namespace Pulsar.Common.Tests.DNS
{
    [TestClass]
    public class HostsConverterTests
    {
        private readonly HostsConverter _converter = new HostsConverter();

        [TestMethod]
        public void RawHostsToList_NullOrEmpty_ReturnsEmptyList()
        {
            Assert.AreEqual(0, _converter.RawHostsToList(null).Count);
            Assert.AreEqual(0, _converter.RawHostsToList(string.Empty).Count);
        }

        [TestMethod]
        public void RawHostsToList_SingleEntry_ParsesHostnameAndPort()
        {
            var hosts = _converter.RawHostsToList("example.com:4782;");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("example.com", hosts[0].Hostname);
            Assert.AreEqual((ushort)4782, hosts[0].Port);
        }

        [TestMethod]
        public void RawHostsToList_MultipleEntries_ParsesAll()
        {
            var hosts = _converter.RawHostsToList("1.1.1.1:53;host.two:5353;2001:db8::1:443;");

            Assert.AreEqual(3, hosts.Count);
            Assert.AreEqual("1.1.1.1", hosts[0].Hostname);
            Assert.AreEqual((ushort)53, hosts[0].Port);
            Assert.AreEqual("host.two", hosts[1].Hostname);
            Assert.AreEqual((ushort)5353, hosts[1].Port);
            // IPv6-ish entry: everything up to the LAST ':' is the hostname.
            Assert.AreEqual("2001:db8::1", hosts[2].Hostname);
            Assert.AreEqual((ushort)443, hosts[2].Port);
        }

        [TestMethod]
        public void RawHostsToList_EmptySegments_AreSkipped()
        {
            var hosts = _converter.RawHostsToList(";;example.com:4782;;");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("example.com", hosts[0].Hostname);
        }

        [TestMethod]
        public void RawHostsToList_Whitespace_PortIsToleratedHostnameIsNotTrimmed()
        {
            // ushort.TryParse tolerates surrounding whitespace in the port, but the
            // hostname substring is never trimmed - current behavior documented, not trimmed by design.
            var hosts = _converter.RawHostsToList("  1.2.3.4 : 4782  ;");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("  1.2.3.4 ", hosts[0].Hostname);
            Assert.AreEqual((ushort)4782, hosts[0].Port);
        }

        [TestMethod]
        public void RawHostsToList_BareHostnameWithoutPort_IsAcceptedWithPortZero()
        {
            // Surprising but actual behavior: an entry without ':' is kept with Port = 0, not rejected.
            var hosts = _converter.RawHostsToList("example.com;");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("example.com", hosts[0].Hostname);
            Assert.AreEqual((ushort)0, hosts[0].Port);
        }

        [TestMethod]
        public void RawHostsToList_MalformedPorts_AreSilentlySkipped()
        {
            // Missing port, non-numeric port and out-of-range port entries are dropped without throwing.
            var hosts = _converter.RawHostsToList("example.com:;bad:abc;overflow:99999;good:4782;");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("good", hosts[0].Hostname);
            Assert.AreEqual((ushort)4782, hosts[0].Port);
        }

        [TestMethod]
        public void RawHostsToList_SingleHttpUrlWithoutSemicolon_ReturnsUrlAsHostname()
        {
            var hosts = _converter.RawHostsToList("https://example.com/pulsar");

            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("https://example.com/pulsar", hosts[0].Hostname);
            Assert.AreEqual((ushort)0, hosts[0].Port);
        }

        [TestMethod]
        public void RawHostsToList_MixedUrlsAndHostPorts_ParsesEachKind()
        {
            var hosts = _converter.RawHostsToList("http://a.example/x;1.2.3.4:4782;plain.host;");

            Assert.AreEqual(3, hosts.Count);
            Assert.AreEqual("http://a.example/x", hosts[0].Hostname);
            Assert.AreEqual((ushort)0, hosts[0].Port);
            Assert.AreEqual("1.2.3.4", hosts[1].Hostname);
            Assert.AreEqual((ushort)4782, hosts[1].Port);
            Assert.AreEqual("plain.host", hosts[2].Hostname);
            Assert.AreEqual((ushort)0, hosts[2].Port);
        }

        [TestMethod]
        public void ListToRawHosts_RoundTrip_PreservesHostnameAndPort()
        {
            var original = new List<Host>
            {
                new Host { Hostname = "example.com", Port = 4782 },
                new Host { Hostname = "1.2.3.4", Port = 443 }
            };

            var raw = _converter.ListToRawHosts(original);

            Assert.AreEqual("example.com:4782;1.2.3.4:443;", raw);

            var roundTrip = _converter.RawHostsToList(raw);
            Assert.AreEqual(original.Count, roundTrip.Count);
            for (var i = 0; i < original.Count; i++)
            {
                Assert.AreEqual(original[i].Hostname, roundTrip[i].Hostname);
                Assert.AreEqual(original[i].Port, roundTrip[i].Port);
            }
        }

        [TestMethod]
        public void ListToRawHosts_EmptyList_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _converter.ListToRawHosts(new List<Host>()));
        }
    }
}
