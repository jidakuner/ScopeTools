using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.DataMining.Tools.ScopeUtils.UT
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            List<int> list = new List<int>();

            double output = list.DefaultIfEmpty().Average();
            Console.WriteLine(output.ToString());
        }
    }
}
