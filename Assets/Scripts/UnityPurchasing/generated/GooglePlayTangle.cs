// WARNING: Do not modify! Generated file.

namespace UnityEngine.Purchasing.Security {
    public class GooglePlayTangle
    {
        private static byte[] data = System.Convert.FromBase64String("0/trElDut2sAIYtZ4xK2Xu+X7SlmyYzQzmaNuA/bYiFuwFozhDWzcgQ1WONNax5rmPrqKPmhO8XmxdcVQvspj5HgcVudsJ2nwChea3dT99myuZ570LhWS84F+G844q4jtcQZ2ssXb7nHIg/4Qrdra0aaGJ4c/Z1VUxwY82o+8quKa0wIq3qrjyrQJMDDhOPt0x8k1SCQfav7TnPgxRzpKfFyfHND8XJ5cfFycnPtni2wvKcBQ/FyUUN+dXpZ9Tv1hH5ycnJ2c3BptWfV1zAVuq69yZE53Seo6IZMxr+bzfNWB85R2XgKv6JjQK20T0SkLzZfqaK8CL2fAyi4Zx3SdMSkiAcQAl8hJVALB7Xivg+bMLQ9lfPa2b2lhagv8DYpOnFwcnNy");
        private static int[] order = new int[] { 3,5,9,12,13,13,7,8,13,12,13,13,13,13,14 };
        private static int key = 115;

        public static readonly bool IsPopulated = true;

        public static byte[] Data() {
        	if (IsPopulated == false)
        		return null;
            return Obfuscator.DeObfuscate(data, order, key);
        }
    }
}
