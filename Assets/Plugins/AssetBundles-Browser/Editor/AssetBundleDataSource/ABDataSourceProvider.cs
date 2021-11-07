using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AssetBundleBrowser.AssetBundleDataSource
{
    internal class ABDataSourceProviderUtility
    {
        private static List<Type> s_customNodes;

        internal static List<Type> CustomABDataSourceTypes
        {
            get
            {
                if (s_customNodes == null) s_customNodes = BuildCustomABDataSourceList();
                return s_customNodes;
            }
        }

        private static List<Type> BuildCustomABDataSourceList()
        {
            List<Type> properList = new List<Type>();
            properList.Add(null); //empty spot for "default" 
            Assembly[] x = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in x)
                try
                {
                    List<Type> list = new List<Type>(
                        assembly
                            .GetTypes()
                            .Where(t => t != typeof(ABDataSource))
                            .Where(t => typeof(ABDataSource).IsAssignableFrom(t)));


                    for (int count = 0; count < list.Count; count++)
                        if (list[count].Name == "AssetDatabaseABDataSource")
                            properList[0] = list[count];
                        else if (list[count] != null)
                            properList.Add(list[count]);
                }
                catch (Exception)
                {
                    //assembly which raises exception on the GetTypes() call - ignore it
                }


            return properList;
        }
    }
}