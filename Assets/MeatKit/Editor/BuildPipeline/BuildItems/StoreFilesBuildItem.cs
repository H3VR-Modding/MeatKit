using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Build Items/Store Files", fileName = "New build item")]
    public class StoreFilesBuildItem : BuildItem
    {
        public string BundleName;
        public Object[] Items;

        public override IEnumerable<string> RequiredDependencies
        {
            get { return new string[0]; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            if (BundleName != Extensions.MakeValidFileName(BundleName))
                messages["BundleName"] = BuildMessage.Error("Bundle name contains invalid characters.");

            return messages;
        }

        public override List<AssetBundleBuild> ConfigureBuild()
        {
            List<AssetBundleBuild> bundles = new List<AssetBundleBuild>();

            bundles.Add(new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = Items.Select(AssetDatabase.GetAssetPath).ToArray()
            });

            return bundles;
        }
    }
}
