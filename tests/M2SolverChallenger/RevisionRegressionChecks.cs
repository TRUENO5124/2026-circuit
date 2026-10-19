using CircuitAssistant.Engine;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace M2SolverChallenger;

partial class Program
{
    private static async Task RunRevisionRegressionChecks()
    {
        Console.WriteLine("\n[Suite 9] Document audit regression checks");
        foreach (var template in new[]
        {
            CircuitTemplateFactory.CreateCommonEmitterTemplate(), CircuitTemplateFactory.CreateRcTransientTemplate(),
            CircuitTemplateFactory.CreateRcCoupledTemplate(), CircuitTemplateFactory.CreateCommonBaseTemplate()
        })
        {
            var data = CircuitProjectData.From(template.ProjectName, template.ProjectFileName, template.Elements, template.Wires);
            var snapshot = data.ToModel();
            var net = NetlistExtractor.Extract(snapshot.Elements, snapshot.Wires);
            Assert(net.Validate().IsValid, $"9.1 {template.ProjectName}: snapshot has a valid netlist", "Snapshot lost connectivity");
            var classification = TopologyClassifier.Classify(net);
            var result = await classification.Strategy.SolveAsync(net, new AnalysisParameters());
            Assert(result.IsSuccess, $"9.1 {template.ProjectName}: supported template solves", result.SummaryText);
        }

        CircuitProjectData MakeProject()
        {
            var t = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            return CircuitProjectData.From(t.ProjectName, t.ProjectFileName, t.Elements, t.Wires);
        }
        void Reject(Action<CircuitProjectData> mutate, string name)
        {
            var data = MakeProject();
            mutate(data);
            bool rejected = false;
            try { data.ToModel(); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "9.2 Reject " + name, "Malformed project was accepted");
        }
        Reject(d => d.Version = "9.0.0", "unknown version");
        Reject(d => d.Elements[1].Id = d.Elements[0].Id, "duplicate element ID");
        Reject(d => d.Wires[0].StartPinName = "MISSING", "unknown pin");
        Reject(d => { d.Wires[0].StartElementId = "MISSING"; d.Wires[0].StartPinName = "WireTap"; }, "missing tap wire");
        Reject(d => { d.Wires[0].StartElementId = d.Wires[0].Id; d.Wires[0].StartPinName = "WireTap"; }, "self tap");
        Reject(d => d.Wires[0].Waypoints = new() { new[] { 1f } }, "invalid waypoint shape");
        Reject(d => d.Elements[0].DisplayNumber = double.NaN, "non-finite value");
        Reject(d => d.Elements[0].FrequencyHz = 0, "zero frequency");
        Reject(d => d.Elements[0].Material = (SemiconductorMaterial)99, "unknown semiconductor material");

        var legacy = MakeProject();
        legacy.Version = "1.0.0";
        var cap = legacy.Elements.First(e => e.Id == "C1");
        cap.Type = ElementType.CapacitorPolarized;
        cap.Pins.Clear();
        var normalized = legacy.ToModel();
        Assert(normalized.Wires.Where(w => w.StartElementId == "C1").All(w => w.StartPinName is "+" or "-") &&
            normalized.Wires.Where(w => w.EndElementId == "C1").All(w => w.EndPinName is "+" or "-"),
            "9.3 Legacy polarized capacitor aliases normalize", "Numeric pin aliases remain");

        var file = Path.Combine(Path.GetTempPath(), $"circuit-audit-{Guid.NewGuid():N}.circuit");
        try
        {
            var data = MakeProject();
            data.CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5);
            data.ParameterUnits["Rb1"] = "MΩ";
            data.IsTeachingTemplate = true;
            data.Elements[0].Description = "roundtrip";
            data.Elements[0].Value = 0.123;
            data.Elements[0].FrequencyHz = 4321;
            data.Elements.First(e => e.Type == ElementType.BjtNpn).Material = SemiconductorMaterial.Germanium;
            await ProjectManagerService.SaveProjectToFileAsync(file, data);
            // Exercise the replacement path as well as first creation.
            data.Description = "replacement";
            await ProjectManagerService.SaveProjectToFileAsync(file, data);
            var loaded = await ProjectManagerService.LoadProjectFromFileAsync(file);
            Assert(loaded.CreatedAt == data.CreatedAt && loaded.Description == "replacement" && loaded.IsTeachingTemplate,
                "9.4 Project metadata survives atomic replacement", "Metadata changed");
            Assert(loaded.ParameterUnits["Rb1"] == "MΩ" && loaded.Elements[0].FrequencyHz == 4321 &&
                loaded.Elements[0].Description == "roundtrip" && loaded.Elements[0].Value == 0.123,
                "9.4 Units, frequency and element fields roundtrip", "Fields changed");
            Assert(loaded.Version == "1.2.0", "9.4 Project format version is 1.2.0", loaded.Version);
            var materialRoundtrip = loaded.ToModel().Elements.First(e => e.Type == ElementType.BjtNpn);
            Assert(materialRoundtrip.Material == SemiconductorMaterial.Germanium &&
                Math.Abs(materialRoundtrip.ForwardVoltageDrop - 0.3) < 1e-9,
                "9.4 Silicon/germanium material survives save/load and drives the 0.3 V forward drop",
                materialRoundtrip.Material.ToString());
            string original = await File.ReadAllTextAsync(file);
            data.Version = "bad";
            try { await ProjectManagerService.SaveProjectToFileAsync(file, data); } catch (InvalidDataException) { }
            Assert(await File.ReadAllTextAsync(file) == original, "9.4 Rejected save preserves existing file", "Original overwritten");
            await File.WriteAllTextAsync(file, "{}");
            bool rejected = false;
            try { await ProjectManagerService.LoadProjectFromFileAsync(file); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "9.4 Non-project JSON is rejected", "Empty JSON accepted");
        }
        finally { if (File.Exists(file)) File.Delete(file); }

        ElectricalNetlist Build(params (ElementType Type, double Value, string Unit, int[] Nodes)[] items)
        {
            var net = new ElectricalNetlist();
            int id = 0;
            foreach (var item in items)
            {
                var element = new CircuitElement { Id = "E" + id++, Type = item.Type, DisplayNumber = item.Value, UnitSymbol = item.Unit };
                element.InitializeDefaultPins();
                net.Elements.Add(element.Id, element);
                for (int i = 0; i < element.Pins.Count; i++)
                {
                    var n = GetOrCreateNet(net, item.Nodes[i], "N" + item.Nodes[i], isGround: item.Nodes[i] == 0);
                    n.ConnectedPins.Add(new ComponentPinRef(element.Id, element.Pins[i].Id, element.Pins[i].Name));
                }
            }
            return CompleteGraph(net);
        }
        var currentNet = Build((ElementType.DcCurrentSource, 2, "mA", new[] { 1, 0 }),
            (ElementType.Resistor, 3, "kΩ", new[] { 1, 0 }));
        var currentResult = await TopologyClassifier.Classify(currentNet).Strategy.SolveAsync(currentNet, new AnalysisParameters());
        // Display convention (2026-10-18): the raw MNA log shows at most two decimals,
        // so the 6 V node is logged as "= 6 V" (numerically identical).
        Assert(currentResult.IsSuccess && currentResult.RawMnaLog.Contains("= 6 V"),
            "9.5 Current-source direction obeys drawn arrow and KCL", currentResult.RawMnaLog);
        Assert(currentNet.ToSpiceNetlist().Contains("I_E0 0 1 DC 0.002"),
            "9.5 SPICE current source agrees with native direction", currentNet.ToSpiceNetlist());

        var contradictory = Build((ElementType.DcVoltageSource, 5, "V", new[] { 1, 0 }),
            (ElementType.DcVoltageSource, 10, "V", new[] { 1, 0 }));
        var contradictionResult = await TopologyClassifier.Classify(contradictory).Strategy.SolveAsync(contradictory, new AnalysisParameters());
        Assert(!contradictionResult.IsSuccess, "9.5 Contradictory ideal voltage sources fail", contradictionResult.SummaryText);

        var highPass = Build((ElementType.AcVoltageSource, 1, "V", new[] { 1, 0 }),
            (ElementType.Capacitor, 1, "μF", new[] { 1, 2 }), (ElementType.Resistor, 1, "kΩ", new[] { 2, 0 }));
        var hpClassification = TopologyClassifier.Classify(highPass);
        var hp = await hpClassification.Strategy.SolveAsync(highPass, new AnalysisParameters());
        Assert(hp.IsSuccess && hpClassification.SubtypeName == "HighPassFilter" && hp.Steps.Any(s => s.LatexFormula.Contains("s\\tau")),
            "9.6 RC high-pass uses a high-pass transfer function", string.Join(";", hp.Steps.Select(s => s.LatexFormula)));
        highPass.Elements["E2"].DisplayNumber = -1;
        Assert(!highPass.Validate().IsValid, "9.6 Negative resistance fails validation", "Negative resistance accepted");

        var unsupported = Build((ElementType.Diode, 1, "", new[] { 1, 0 }),
            (ElementType.Resistor, 1, "kΩ", new[] { 1, 0 }));
        var unsupportedClass = TopologyClassifier.Classify(unsupported);
        var unsupportedResult = await unsupportedClass.Strategy.SolveAsync(unsupported, new AnalysisParameters());
        Assert(unsupportedClass.TopologyType == CircuitTopologyType.DiodeCircuit &&
               unsupportedClass.Strategy is CircuitAssistant.Engine.Strategies.DiodeCircuitStrategy &&
               !unsupportedResult.IsSuccess &&
               unsupportedResult.SummaryText.Contains("二极管", StringComparison.Ordinal),
            "9.7 Diode network routed to DiodeCircuit teaching strategy (not MNA) and explicitly rejected",
            $"{unsupportedClass.TopologyType}/{unsupportedClass.Strategy.GetType().Name}: {unsupportedResult.SummaryText}");
        var fet = new ElectricalNetlist();
        fet.Elements.Add("Q", new CircuitElement { Id = "Q", Type = ElementType.Mosfet });
        var fetClass = TopologyClassifier.Classify(fet);
        var fetResult = await fetClass.Strategy.SolveAsync(fet, new AnalysisParameters());
        Assert(fetClass.TopologyType == CircuitTopologyType.MosfetAmplifier &&
               fetClass.Strategy is CircuitAssistant.Engine.Strategies.MosfetAmplifierStrategy &&
               !fetResult.IsSuccess &&
               !string.IsNullOrWhiteSpace(fetResult.SummaryText),
            "9.7 FET routed to MosfetAmplifier teaching strategy (not Unsupported/MNA) and explicitly rejected",
            $"{fetClass.TopologyType}/{fetClass.Strategy.GetType().Name}: {fetResult.SummaryText}");
        var cutoff = MakeProject().ToModel();
        cutoff.Elements.First(e => e.Id == "VCC").DisplayNumber = 1;
        var cutoffNet = NetlistExtractor.Extract(cutoff.Elements, cutoff.Wires);
        Assert(!(await TopologyClassifier.Classify(cutoffNet).Strategy.SolveAsync(cutoffNet, new AnalysisParameters())).IsSuccess,
            "9.7 Cutoff BJT does not produce small-signal results", "Cutoff reported active");

        var lowCurrentTemplate = CircuitTemplateFactory.CreateCommonBaseTemplate(rb1: 30, rb2: 15, re: 2, beta: 50);
        foreach (var resistor in lowCurrentTemplate.Elements.Where(e => e.Type == ElementType.Resistor))
        {
            resistor.DisplayNumber *= 10;
            resistor.UnitSymbol = "MΩ"; // 10,000 times the original resistance, same DC voltage ratios.
        }
        var lowCurrentNet = NetlistExtractor.Extract(lowCurrentTemplate.Elements, lowCurrentTemplate.Wires);
        var lowCurrent = await TopologyClassifier.Classify(lowCurrentNet).Strategy.SolveAsync(lowCurrentNet, new AnalysisParameters());
        // 教材估算法（用户口径 2026-10-18）：每个中间量按显示单位两位小数舍入后回代。
        // 该用例把全部电阻放大 1e4 倍（Re=20 MΩ 等），使 I_EQ 落在亚微安级、I_BQ 舍入为 0，
        // 用于检查教学估算法链在小电流下不被钳位，h 参数增益仍按同一链路计算。
        const double scaledRb1 = 300e6, scaledRb2 = 150e6, scaledRe = 20e6, scaledRc = 30e6, scaledRl = 30e6;
        double ubScaled = TeachingDisplay.RoundTo(12.0 * scaledRb2 / (scaledRb1 + scaledRb2), QuantityKind.Voltage);
        double ieqScaled = TeachingDisplay.RoundTo((ubScaled - 0.7) / scaledRe, QuantityKind.Current);
        double rlPrimeScaled = TeachingDisplay.RoundTo(scaledRc * scaledRl / (scaledRc + scaledRl), QuantityKind.Resistance);
        double hieScaled = TeachingDisplay.RoundTo(50.0 + 51.0 * 0.026 / ieqScaled, QuantityKind.Resistance);
        double expectedGain = TeachingDisplay.RoundPlain(50.0 * rlPrimeScaled / hieScaled);
        double actualGain = double.TryParse(lowCurrent.AcParameters.VoltageGain, System.Globalization.CultureInfo.InvariantCulture, out var gain) ? gain : double.NaN;
        Assert(lowCurrent.IsSuccess && Math.Abs(actualGain - expectedGain) < 0.1,
            "9.7 Sub-microamp emitter current is not clamped to one microamp", lowCurrent.AcParameters.VoltageGain);

        var wires = new List<CircuitWire>
        {
            new() { Id = "A", EndElementId = "B", EndPinName = "WireTap" },
            new() { Id = "B", EndElementId = "A", EndPinName = "WireTap" },
            new() { Id = "C", StartElementId = "B", StartPinName = "WireTap" }, new() { Id = "D" }
        };
        CircuitWire.RemoveWithDescendants(wires, new[] { "A" });
        Assert(wires.Count == 1 && wires[0].Id == "D", "9.8 Removing a wire removes all dependent taps, including cycles", "Dangling taps remain");
        Assert(ScientificNotationFormatter.ToLatexScientific(1e-18).Contains("-18"),
            "9.8 Tiny nonzero value stays nonzero", ScientificNotationFormatter.ToLatexScientific(1e-18));
        await RunViewModelRegressionChecks();
    }

    private static async Task RunViewModelRegressionChecks()
    {
        Console.WriteLine("\n[Suite 10] Real ViewModel project lifecycle, without window automation");
        var vm = new CircuitAssistant.ViewModels.AnalysisStudioViewModel();
        Assert(!vm.HasActiveProject && vm.Elements.Count == 0 && !vm.Result.IsSuccess && vm.Result.Steps.Count == 0,
            "10.1 Startup has no active project or fictitious result", vm.StatusMessage);
        Assert(!vm.HasAnalysisError && vm.FooterStatusLabel == "提示",
            "10.1a Startup has no false ERROR banner", vm.FooterStatusMessage);
        await vm.CreateDefaultProject();
        Assert(vm.HasActiveProject && vm.IsProjectModified && vm.CurrentProjectFilePath == null,
            "10.2 New template is unsaved with no previous path", vm.StatusMessage);
        var source = vm.Elements.First(e => e.IsAcSource);
        double amplitude = source.DisplayNumber;
        vm.FreqNumber = 2;
        Assert(source.FrequencyHz == 2000 && source.DisplayNumber == amplitude && source.UnitSymbol == "mV",
            "10.3 Frequency macro preserves source amplitude", source.FormattedValueDisplay);
        vm.SelectedElement = source;
        source.FrequencyHz = 3000;
        Assert(vm.FreqNumber * vm.SelectedFreqUnit.Multiplier == 3000,
            "10.4 Inspector frequency synchronizes macro", vm.FreqNumber.ToString());
        source.Pins[0].ConnectedNetId = 12345;
        await vm.SolveCircuitAsync();
        Assert(vm.Result.IsSuccess && vm.Result.Steps.Count == 5 && !vm.IsAnalyzing,
            "10.5 Real solve command publishes successful result", vm.StatusMessage);
        Assert(source.Pins[0].ConnectedNetId == 12345,
            "10.6 Background extractor does not mutate editor pin nets", "Editor pin net changed");
        var statusNotifications = new List<string?>();
        vm.PropertyChanged += (_, e) => statusNotifications.Add(e.PropertyName);
        var resistor = vm.Elements.First(e => e.Type == ElementType.Resistor);
        double originalResistance = resistor.DisplayNumber;
        resistor.DisplayNumber = -1;
        await vm.SolveCircuitAsync();
        Assert(!vm.Result.IsSuccess && vm.HasAnalysisError && vm.FooterStatusLabel == "ERROR" &&
            vm.FooterStatusMessage.Contains("无法完成求解"),
            "10.6a Failed real analysis exposes bottom ERROR diagnostic", vm.FooterStatusMessage);
        string errorMessage = vm.FooterStatusMessage;
        vm.StatusMessage = "已选中元件";
        Assert(vm.HasAnalysisError && vm.FooterStatusMessage == errorMessage,
            "10.6b Selection status cannot hide unresolved analysis error", vm.FooterStatusMessage);
        Assert(statusNotifications.Contains(nameof(vm.HasAnalysisError)) &&
            statusNotifications.Contains(nameof(vm.FooterStatusMessage)) && statusNotifications.Contains(nameof(vm.FooterStatusLabel)),
            "10.6c Footer bindings receive error state notifications", string.Join(",", statusNotifications));
        resistor.DisplayNumber = originalResistance;
        Assert(!vm.HasAnalysisError && vm.FooterStatusLabel != "ERROR" && vm.FooterStatusMessage.Contains("重新求解"),
            "10.6d Circuit edit clears stale error, including failures without steps", vm.FooterStatusMessage);
        await vm.SolveCircuitAsync();
        Assert(vm.Result.IsSuccess && !vm.HasAnalysisError && vm.FooterStatusLabel == "完成",
            "10.6e Corrected circuit returns footer to completed state", vm.FooterStatusMessage);
        source.DisplayNumber += 1;
        Assert(!vm.Result.IsSuccess && vm.Result.Steps.Count == 0 && vm.IsProjectModified,
            "10.7 Editing invalidates previous solution", vm.Result.SummaryText);

        // BJT small-signal model parameters r_bb' / r_be(h_ie) are editable teaching
        // parameters: 0 = auto default, positive = explicit problem value.
        var bjt = vm.Elements.First(e => e.SupportsBjtSmallSignalParameters);
        Assert(Math.Abs(bjt.BjtRbb) < 1e-9 && Math.Abs(bjt.BjtRbe) < 1e-9 && Math.Abs(vm.RbbNumber) < 1e-9,
            "10.7b BJT 小信号参数默认自动（0 = 共射/共集 200 Ω、共基 50 Ω；rbe 按 Q 点计算）",
            $"{bjt.BjtRbb}/{bjt.BjtRbe}/vm={vm.RbbNumber}");
        vm.RbbNumber = 300;
        vm.RbeNumber = 1500;
        Assert(Math.Abs(bjt.BjtRbb - 300) < 1e-9 && Math.Abs(bjt.BjtRbe - 1500) < 1e-9,
            "10.7c rbb/rbe 宏参数同步到 BJT 元件", $"{bjt.BjtRbb}/{bjt.BjtRbe}");
        Assert(!vm.Result.IsSuccess, "10.7d 修改 rbb/rbe 使旧结果失效", vm.Result.SummaryText);

        string file = Path.Combine(Path.GetTempPath(), $"circuit-vm-{Guid.NewGuid():N}.circuit");
        try
        {
            string projectNameBeforeSave = vm.ProjectName;
            vm.CurrentProjectFilePath = file; // Known path exercises the real file service without a picker.
            await vm.SaveProjectAsync();
            Assert(File.Exists(file) && !vm.IsProjectModified, "10.8 Save command writes and clears modification state", vm.StatusMessage);
            var savedProject = await ProjectManagerService.LoadProjectFromFileAsync(file);
            Assert(savedProject.ProjectName == projectNameBeforeSave && vm.ProjectName == projectNameBeforeSave,
                "10.8b 保存保留工程标题（旧缺陷：每次保存把 ProjectName 改写为文件名）",
                $"before='{projectNameBeforeSave}', vm='{vm.ProjectName}', file='{savedProject.ProjectName}'");
            var savedBjt = savedProject.ToModel().Elements.First(e => e.SupportsBjtSmallSignalParameters);
            Assert(Math.Abs(savedBjt.BjtRbb - 300) < 1e-9 && Math.Abs(savedBjt.BjtRbe - 1500) < 1e-9,
                "10.8c BJT 的 rbb/rbe 随工程保存并读回", $"{savedBjt.BjtRbb}/{savedBjt.BjtRbe}");
            vm.SelectedElement = null;
            source.IsSelected = true;
            vm.Wires[0].NetId += 1;
            Assert(!vm.IsProjectModified, "10.9 Selection and derived net IDs do not dirty a saved project", vm.StatusMessage);
            double previous = source.DisplayNumber;
            vm.PushUndo();
            source.DisplayNumber += 5;
            vm.Undo();
            Assert(vm.Elements.First(e => e.Id == source.Id).DisplayNumber == previous,
                "10.10 Undo restores numeric edit", "Wrong restored value");
            vm.Redo();
            Assert(vm.Elements.First(e => e.Id == source.Id).DisplayNumber == previous + 5,
                "10.11 Redo restores numeric edit", "Wrong redone value");
            vm.ConfirmDiscardChangesAsync = () => Task.FromResult(false);
            await vm.CloseProject();
            Assert(vm.HasActiveProject && vm.CurrentProjectFilePath == file,
                "10.12 Cancelled close preserves project and path", vm.StatusMessage);
            string original = await File.ReadAllTextAsync(file);
            vm.ConfirmDiscardChangesAsync = () => Task.FromResult(true);
            await vm.CreateBlankProject();
            Assert(vm.HasActiveProject && vm.Elements.Count == 0 && vm.Wires.Count == 0 && vm.CurrentProjectFilePath == null && vm.IsProjectModified,
                "10.13 New project cannot inherit previous save path", vm.StatusMessage);
            vm.Undo();
            Assert(vm.Elements.Count == 0 && await File.ReadAllTextAsync(file) == original,
                "10.14 New project clears history and preserves previous file", "History or previous file changed");
            await vm.CloseProject();
            Assert(!vm.HasActiveProject && !vm.IsProjectModified && vm.CurrentProjectFilePath == null,
                "10.15 Confirmed close resets lifecycle state", vm.StatusMessage);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
