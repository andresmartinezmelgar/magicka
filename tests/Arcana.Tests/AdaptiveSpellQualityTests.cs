using Arcana.Client;

internal static class AdaptiveSpellQualityTests
{
    internal static void Run(Action<string, Action> test)
    {
        static void Check(bool condition, string reason)
        { if (!condition) throw new Exception(reason); }

        test("Spell quality: old and limited mobile GPUs keep the previous effects", () =>
        {
            foreach (string gpu in new[] { "Adreno (TM) 610", "Adreno 530", "Mali-G57 MC2", "Mali-T880", "PowerVR GE8320", "SwiftShader" })
            {
                var policy = new AdaptiveSpellQuality(true, gpu, 8);
                for (int i = 0; i < 20; i++) policy.Observe(60, 16, 2);
                Check(policy.LegacyOnly && !policy.Enhanced, gpu + " left the conservative profile.");
            }
            Check(new AdaptiveSpellQuality(true, "Unknown", 4).LegacyOnly, "Limited CPU ignored.");
            Check(!new AdaptiveSpellQuality(false, "llvmpipe", 4).LegacyOnly, "Desktop inspection was classified as a phone.");
        });
        test("Spell quality: capable and unknown devices are decided by real performance", () =>
        {
            foreach (string gpu in new[] { "Adreno (TM) 830", "Adreno 660", "Mali-G715", "Unknown", "" })
            {
                var policy = new AdaptiveSpellQuality(true, gpu, 8);
                Check(policy.Enhanced && !policy.LegacyOnly, "Unexpected hardware veto: " + gpu);
                policy.Observe(35, 45, 2);
                Check(!policy.Enhanced, "Slow unknown/capable device did not fall back.");
            }
        });
        test("Spell quality: immediate downgrade and twelve healthy seconds before recovery", () =>
        {
            var policy = new AdaptiveSpellQuality(false, "", 8);
            policy.Observe(40, 34, 2);
            Check(!policy.Enhanced, "Slow window kept expensive effects.");
            for (int i = 0; i < 5; i++) { policy.Observe(60, 17, 2); Check(!policy.Enhanced, "Recovered too early."); }
            policy.Observe(60, 17, 2); Check(policy.Enhanced, "Stable recovery never completed.");
            policy.Observe(60, 44, 2); Check(!policy.Enhanced, "Good average hid frame-time spikes.");
        });
        test("Spell quality: fluctuating FPS cannot alternate the two styles", () =>
        {
            var policy = new AdaptiveSpellQuality(false, "", 8);
            policy.Observe(35, 40, 2);
            for (int i = 0; i < 12; i++)
            {
                policy.Observe(i % 2 == 0 ? 60 : 52, i % 2 == 0 ? 17 : 24, 2);
                Check(!policy.Enhanced, "Unstable scene re-enabled expensive effects.");
            }
            for (int i = 0; i < 6; i++) policy.Observe(60, 17, 2);
            Check(policy.Enhanced, "Stable scene could not recover after fluctuations.");
        });
        test("Spell quality: deliberate thirty-FPS cap is not a performance collapse", () =>
        {
            var policy = new AdaptiveSpellQuality(false, "", 8);
            policy.Observe(30, 35, 2, 30); Check(policy.Enhanced, "Thirty-FPS cap forced the fallback.");
            policy.Observe(20, 58, 2, 30); Check(!policy.Enhanced, "Actual drop below the cap was ignored.");
            for (int i = 0; i < 6; i++) policy.Observe(30, 35, 2, 30);
            Check(policy.Enhanced, "Could not recover at the configured cap.");
        });
        test("Spell quality: invalid samples and long pauses do not fake recovery", () =>
        {
            var policy = new AdaptiveSpellQuality(false, "", 8);
            policy.Observe(float.NaN, 16, 2); policy.Observe(60, float.PositiveInfinity, 2);
            policy.Observe(10, 100, double.NaN); Check(policy.Enhanced, "Invalid measurement changed the profile.");
            policy.Observe(30, 40, 2); policy.Observe(60, 16, 120);
            Check(!policy.Enhanced, "One sample after a long pause counted as twelve stable seconds.");
        });
    }
}
