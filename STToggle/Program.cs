using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;

namespace STToggle
{
    public class Program
    {
        private static readonly string baseName = "STToggle";
        private static readonly ModKey baseModKey = ModKey.FromNameAndExtension($"{baseName}.esp");
        private static readonly uint magicEffectSingleCastTemplateFormId = 0x001D8C;
        private static readonly uint magicEffectDualCastTemplateFormId = 0x00285E;
        private static readonly uint perkTemplateFormId = 0x001D8E;
        private static readonly uint spellTemplateFormId = 0x00285C;
        private static readonly uint effectsSpellTemplateFormId = 0x00434D;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddRunnabilityCheck(CheckRunnability)
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, $"{baseName}_Patch2.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var basePlugin = state.LoadOrder.GetIfEnabledAndExists(baseModKey);
            var spellsJson = JsonConvert.DeserializeObject<List<PluginSpells>>(File.ReadAllText("spells.json"));

#pragma warning disable CS8602 // Dereference of a possibly null reference.
            foreach (var plugin in spellsJson)
            {
                var modKey = ModKey.FromNameAndExtension(plugin.Plugin);
                Console.WriteLine($"Adding spells from {plugin.Plugin}");
                foreach (var spellId in plugin.Spells)
                {
                    Console.Write($"Adding spell {spellId}");
                    uint spellFormId = Convert.ToUInt32(spellId, 16);
                    var spellLink = new FormLink<ISpellGetter>(modKey.MakeFormKey(spellFormId));
                    var spell = spellLink.Resolve(state.LinkCache);
                    var spellEditorName = spell.EditorID;
                    Console.WriteLine($" ({spell.Name})");
                    
                    // create cost spell
                    var templateSpell = new FormLink<ISpellGetter>(baseModKey.MakeFormKey(spellTemplateFormId)).Resolve(state.LinkCache);
                    var newSpell = templateSpell.Duplicate(state.PatchMod.GetNextFormKey());
                    newSpell.EditorID = $"STMaintainCostSpell{spellEditorName}";
                    newSpell.Name = $"Maintain {spell.Name}";
                    state.PatchMod.Spells.Add(newSpell);

                    // create perk
                    var templatePerk = new FormLink<IPerkGetter>(baseModKey.MakeFormKey(perkTemplateFormId)).Resolve(state.LinkCache);
                    var newPerk = templatePerk.Duplicate(state.PatchMod.GetNextFormKey());
                    newPerk.EditorID = $"ST{spellEditorName}Cost";

                    var modSpellCostEffect = newPerk.Effects.Single(e => e.Priority == 0);
                    modSpellCostEffect.Conditions.ForEach(c => ((FunctionConditionData)c.Conditions.Single().Data).ParameterOneRecord = spellLink);

                    var modSpellMagnitudeEffect = newPerk.Effects.Single(e => e.Priority == 1);
                    FunctionConditionData data = (FunctionConditionData)modSpellMagnitudeEffect.Conditions.Single().Conditions.Single().Data;
                    data.ParameterOneRecord = newSpell.FormKey.AsLink<ISpellGetter>();

                    state.PatchMod.Perks.Add(newPerk);

                    // create magic effects
                    var templateMagicEffect = new FormLink<IMagicEffectGetter>(baseModKey.MakeFormKey(magicEffectSingleCastTemplateFormId)).Resolve(state.LinkCache);
                    var newMagicEffect = templateMagicEffect.Duplicate(state.PatchMod.GetNextFormKey());
                    newMagicEffect.EditorID = $"STMaintainSpellEffect{spellEditorName}";
                    ((ScriptObjectProperty)newMagicEffect.VirtualMachineAdapter.Scripts.Single().Properties.Single(p => p.Name == "CostPerk")).Object.FormKey = newPerk.FormKey;
                    ((ScriptObjectProperty)newMagicEffect.VirtualMachineAdapter.Scripts.Single().Properties.Single(p => p.Name == "CostSpell")).Object.FormKey = newSpell.FormKey;
                    ((ScriptObjectProperty)newMagicEffect.VirtualMachineAdapter.Scripts.Single().Properties.Single(p => p.Name == "ParentEffect")).Object.FormKey = spell.Effects.First().BaseEffect.FormKey;
                    ((ScriptObjectProperty)newMagicEffect.VirtualMachineAdapter.Scripts.Single().Properties.Single(p => p.Name == "ParentSpell")).Object.FormKey = spell.FormKey;

                    var newMagicEffectDualCast = newMagicEffect.Duplicate(state.PatchMod.GetNextFormKey());
                    newMagicEffectDualCast.EditorID = $"STMaintainSpellEffect{spellEditorName}DualCast";
                    ((ConditionFloat)newMagicEffectDualCast.Conditions.First()).ComparisonValue = 1;

                    state.PatchMod.MagicEffects.Add(newMagicEffect);
                    state.PatchMod.MagicEffects.Add(newMagicEffectDualCast);

                    // add effects to spell
                    var modifiedSpell = spell.DeepCopy();
                    var templateEffectsSpell = new FormLink<ISpellGetter>(baseModKey.MakeFormKey(effectsSpellTemplateFormId)).Resolve(state.LinkCache).Duplicate(new FormKey());
                    var templateMagicDualCastEffect = new FormLink<IMagicEffectGetter>(baseModKey.MakeFormKey(magicEffectDualCastTemplateFormId)).Resolve(state.LinkCache);
                    var templateSingleCastEffect = templateEffectsSpell.Effects.Single(e => e.BaseEffect.FormKey == templateMagicEffect.FormKey);
                    templateSingleCastEffect.BaseEffect.FormKey = newMagicEffect.FormKey;
                    var templateDualCastEffect = templateEffectsSpell.Effects.Single(e => e.BaseEffect.FormKey == templateMagicDualCastEffect.FormKey);
                    templateDualCastEffect.BaseEffect.FormKey = newMagicEffectDualCast.FormKey;
                    modifiedSpell.Effects.Add(templateSingleCastEffect);
                    modifiedSpell.Effects.Add(templateDualCastEffect);
                    state.PatchMod.Spells.Add(modifiedSpell);
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        public static void CheckRunnability(IRunnabilityState state)
        {
            state.LoadOrder.AssertHasMod(baseModKey);
        }
    }
}
