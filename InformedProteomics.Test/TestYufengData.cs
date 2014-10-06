﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using InformedProteomics.Backend.Data.Biology;
using InformedProteomics.Backend.Data.Composition;
using InformedProteomics.Backend.Data.Enum;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.MassSpecData;
using InformedProteomics.Backend.Utils;
using InformedProteomics.TopDown.Scoring;
using NUnit.Framework;
using InformedProteomics.Backend.SequenceTag;

namespace InformedProteomics.Test
{
    [TestFixture]
    public class TestYufengData
    {
        public const string TestRawFilePath = @"\\protoapps\UserData\Sangtae\Yufeng\raw\yufeng_column_test2.raw";
        private static readonly IEnumerable<int> Ms2ScanNums = new[] { 46454, 46475, 46484, 46506, 46562, 46661 };
        private class SimpleMs1Filter : ISequenceFilter
        {
            public IEnumerable<int> GetMatchingMs2ScanNums(double sequenceMass)
            {
                return Ms2ScanNums;
            }
        }

        [Test]
        public void Test43KProtein()
        {
            // Configure amino acid set
            var acetylN = new SearchModification(Modification.Acetylation, '*', SequenceLocation.ProteinNTerm, false);
            var oxM = new SearchModification(Modification.Oxidation, 'M', SequenceLocation.Everywhere, false);
            var dehydroC = new SearchModification(Modification.Dehydro, 'C', SequenceLocation.Everywhere, false);
            var glutathioneC = new SearchModification(Modification.Glutathione, 'C', SequenceLocation.Everywhere, false);
            var dethiomethylM = new SearchModification(Modification.Dethiomethyl, 'M', SequenceLocation.Everywhere, false);
            var deamidatedN = new SearchModification(Modification.Deamidation, 'N', SequenceLocation.Everywhere, false);
            var deamidatedQ = new SearchModification(Modification.Deamidation, 'Q', SequenceLocation.Everywhere, false);
            var pyroCarbamidomethylC = new SearchModification(Modification.PyroCarbamidomethyl, 'C',
                SequenceLocation.ProteinNTerm, false);
            var phosphoS = new SearchModification(Modification.Phosphorylation, 'S', SequenceLocation.Everywhere, false);
            var phosphoT = new SearchModification(Modification.Phosphorylation, 'T', SequenceLocation.Everywhere, false);
            var phosphoY = new SearchModification(Modification.Phosphorylation, 'Y', SequenceLocation.Everywhere, false);
            var nitrosylC = new SearchModification(Modification.Nitrosyl, 'C', SequenceLocation.Everywhere, false);
            var nethylmaleimideC = new SearchModification(Modification.Nethylmaleimide, 'C', SequenceLocation.Everywhere, false);

            const int numMaxModsPerProtein = 4;
            var searchModifications = new List<SearchModification>
            {
                dehydroC,
                glutathioneC,
                oxM,
                dethiomethylM,
                acetylN,
                //phosphoS,
                //phosphoT,
                //phosphoY,
                deamidatedN,
//                deamidatedQ,
                glutathioneC,
                pyroCarbamidomethylC,
                nitrosylC,
                nethylmaleimideC
            };
            var aaSet = new AminoAcidSet(searchModifications, numMaxModsPerProtein);
//            var aaSet = new AminoAcidSet();


            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath);
            const string protSequence =
                "AIPQSVEGQSIPSLAPMLERTTPAVVSVAVSGTHVSKQRVPDVFRYFFGPNAPQEQVQERPFRGLGSGVIIDADKGYIVTNNHVIDGADDIQVGLHDGREVKAKLIGTDSESDIALLQIEAKNLVAIKTSDSDELRVGDFAVAIGNPFGLGQTVTSGIVSALGRSGLGIEMLENFIQTDAAINSGNSGGALVNLKGELIGINTAIVAPNGGNVGIGFAIPANMVKNLIAQIAEHGEVRRGVLGIAGRDLDSQLAQGFGLDTQHGGFVNEVSAGSAAEKAGIKAGDIIVSVDGRAIKSFQELRAKVATMGAGAKVELGLIRDGDKKTVNVTLGEANQTTEKAAGAVHPMLQGASLENASKGVEITDVAQGSPAAMSGLQKGDLIVGINRTAVKDLKSLKELLKDQEGAVALKIVRGKSMLYLVLR";
            const string annotation = "_." + protSequence + "._";
            var seqGraph = SequenceGraph.CreateGraph(aaSet, AminoAcid.ProteinNTerm, protSequence, AminoAcid.ProteinCTerm);
            if (seqGraph == null) return;

            var ms1Filter = new SimpleMs1Filter();
            var ms2ScorerFactory = new ProductScorerBasedOnDeconvolutedSpectra(run);
            foreach(var ms2ScanNum in Ms2ScanNums) ms2ScorerFactory.DeconvoluteProductSpectrum(ms2ScanNum);

            for (var numNTermCleavages = 0; numNTermCleavages <= 0; numNTermCleavages++)
            {
                if (numNTermCleavages > 0) seqGraph.CleaveNTerm();
                var numProteoforms = seqGraph.GetNumProteoforms();
                var modCombs = seqGraph.GetModificationCombinations();
                for (var modIndex = 0; modIndex < numProteoforms; modIndex++)
                {
                    seqGraph.SetSink(modIndex);
                    var protCompositionWithH2O = seqGraph.GetSinkSequenceCompositionWithH2O();
                    var sequenceMass = protCompositionWithH2O.Mass;
                    var modCombinations = modCombs[modIndex];

                    foreach (var ms2ScanNum in ms1Filter.GetMatchingMs2ScanNums(sequenceMass))
                    {
                        var spec = run.GetSpectrum(ms2ScanNum) as ProductSpectrum;
                        if (spec == null) continue;
                        var charge =
                            (int)
                                Math.Round(sequenceMass /
                                           (spec.IsolationWindow.IsolationWindowTargetMz - Constants.Proton));
                        var scorer = ms2ScorerFactory.GetMs2Scorer(ms2ScanNum);
                        var score = seqGraph.GetScore(charge, scorer);
                        if (score <= 3) continue;

                        var precursorIon = new Ion(protCompositionWithH2O, charge);
                        var sequence = protSequence.Substring(numNTermCleavages);
                        var pre = numNTermCleavages == 0 ? annotation[0] : annotation[numNTermCleavages + 1];
                        var post = annotation[annotation.Length - 1];

                        Console.WriteLine("{0}.{1}.{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}", pre, sequence, post, ms2ScanNum, modCombinations, 
                            precursorIon.GetMostAbundantIsotopeMz(), precursorIon.Charge, precursorIon.Composition.Mass, score);
                    }
                }
            }
        }

        [Test]
        public void TestDeconvolution()
        {
            const int minScanNum = 46454;   // 635.43
            const int maxScanNum = 46661;   // 638.90

            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath) as PbfLcMsRun;
            if (run == null) return;
            var summedSpec = run.GetSummedMs1Spectrum(minScanNum, maxScanNum);
            summedSpec.FilterNoise(50.0);
//            summedSpec.Display();

            var deconvoluted = ProductScorerBasedOnDeconvolutedSpectra.GetDeconvolutedSpectrum(summedSpec, 2, 45, new Tolerance(10), 0.9, 2);
            deconvoluted.Display();
        }

        [Test]
        public void TestSumMs1Spectra()
        {
            const int minScanNum = 46454;
            const int maxScanNum = 46661;

            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath) as PbfLcMsRun;
            if (run == null) return;
            var summedSpec = run.GetSummedMs1Spectrum(minScanNum, maxScanNum);
            summedSpec.Display();
        }

        [Test]
        public void TestRunningTimeSummingSpectra()
        {
            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath, MassSpecDataType.XCaliburRun, 1.4826, 1.4826) as PbfLcMsRun;

            var sw = new Stopwatch();
            sw.Start();

            const int windowSize = 5;
            foreach (var scanNum in run.GetScanNumbers(1))
            {
                //var spec = run.GetSpectrum(scanNum);
                var spec = run.GetSummedMs1Spectrum(Math.Max(scanNum - windowSize, run.MinLcScan),
                    Math.Min(scanNum + windowSize, run.MaxLcScan));
            }
            sw.Stop();
            var sec = sw.ElapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine(@"{0:f4} sec", sec);
        }

        [Test]
        public void TestSumIsoProfilesAcrossDifferentCharges()
        {
            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath) as PbfLcMsRun;

            //var spec = run.GetSpectrum(46452); // 635.37
            var spec = run.GetSummedMs1Spectrum(46437, 46466);
            var tolerance = new Tolerance(10);

            const string protSequence =
                "AIPQSVEGQSIPSLAPMLERTTPAVVSVAVSGTHVSKQRVPDVFRYFFGPNAPQEQVQERPFRGLGSGVIIDADKGYIVTNNHVIDGADDIQVGLHDGREVKAKLIGTDSESDIALLQIEAKNLVAIKTSDSDELRVGDFAVAIGNPFGLGQTVTSGIVSALGRSGLGIEMLENFIQTDAAINSGNSGGALVNLKGELIGINTAIVAPNGGNVGIGFAIPANMVKNLIAQIAEHGEVRRGVLGIAGRDLDSQLAQGFGLDTQHGGFVNEVSAGSAAEKAGIKAGDIIVSVDGRAIKSFQELRAKVATMGAGAKVELGLIRDGDKKTVNVTLGEANQTTEKAAGAVHPMLQGASLENASKGVEITDVAQGSPAAMSGLQKGDLIVGINRTAVKDLKSLKELLKDQEGAVALKIVRGKSMLYLVLR";
            const string annotation = "_." + protSequence + "._";
            var seqGraph = SequenceGraph.CreateGraph(new AminoAcidSet(), AminoAcid.ProteinNTerm, protSequence, AminoAcid.ProteinCTerm);
            if (seqGraph == null) return;
            seqGraph.SetSink(0);
            var neutral = seqGraph.GetSinkSequenceCompositionWithH2O();

            var theoProfile = neutral.GetIsotopomerEnvelope();
            var expProfile = new double[theoProfile.Length];
            for (var charge = 22; charge <= 45; charge++)
            {
                var ion = new Ion(neutral, charge);
                var isotopePeaks = spec.GetAllIsotopePeaks(ion, tolerance, 0.1);
                if (isotopePeaks == null) continue;
                Assert.True(isotopePeaks.Length == theoProfile.Length);
                for (var i = 0; i < isotopePeaks.Length; i++)
                {
                    if (isotopePeaks[i] != null) expProfile[i] += isotopePeaks[i].Intensity;
                }
            }
            for (var i = 0; i < theoProfile.Length; i++)
            {
                Console.WriteLine("{0}\t{1}\t{2}", neutral.GetIsotopeMass(i), theoProfile[i], expProfile[i]/expProfile.Max());
            }
            Console.WriteLine("Corr: " + FitScoreCalculator.GetPearsonCorrelation(theoProfile, expProfile));
        }

        [Test]
        public void TestSmartIsoWindowSumming()
        {
            const string protSequence =
                "AIPQSVEGQSIPSLAPMLERTTPAVVSVAVSGTHVSKQRVPDVFRYFFGPNAPQEQVQERPFRGLGSGVIIDADKGYIVTNNHVIDGADDIQVGLHDGREVKAKLIGTDSESDIALLQIEAKNLVAIKTSDSDELRVGDFAVAIGNPFGLGQTVTSGIVSALGRSGLGIEMLENFIQTDAAINSGNSGGALVNLKGELIGINTAIVAPNGGNVGIGFAIPANMVKNLIAQIAEHGEVRRGVLGIAGRDLDSQLAQGFGLDTQHGGFVNEVSAGSAAEKAGIKAGDIIVSVDGRAIKSFQELRAKVATMGAGAKVELGLIRDGDKKTVNVTLGEANQTTEKAAGAVHPMLQGASLENASKGVEITDVAQGSPAAMSGLQKGDLIVGINRTAVKDLKSLKELLKDQEGAVALKIVRGKSMLYLVLR";
            const string annotation = "_." + protSequence + "._";
            var seqGraph = SequenceGraph.CreateGraph(new AminoAcidSet(), AminoAcid.ProteinNTerm, protSequence, AminoAcid.ProteinCTerm);
            if (seqGraph == null) return;
            seqGraph.SetSink(0);
            var neutral = seqGraph.GetSinkSequenceCompositionWithH2O();
            var ion = new Ion(neutral, 43);
            var tolerance = new Tolerance(10);

            const int targetMs2ScanNum = 46562;
            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath) as PbfLcMsRun;
            var ms2Spec = run.GetSpectrum(targetMs2ScanNum) as ProductSpectrum;
            Assert.True(ms2Spec != null);
            var isoWindow = ms2Spec.IsolationWindow;
            //var prevScanNum = run.GetPrevScanNum(targetMs2ScanNum, 1);
            //var nextScanNum = run.GetNextScanNum(targetMs2ScanNum, 1);
            var summedSpec = run.GetSummedMs1Spectrum(targetMs2ScanNum, 2.5);
            //var windowSpec = summedSpec.GetPeakListWithin(isoWindow.MinMz, isoWindow.MaxMz);
            Console.WriteLine("Corr: " + summedSpec.GetCorrScore(ion, tolerance));
        }

        [Test]
        public void TestCorrelation()
        {
            
        }

        [Test]
        public void GetIsoProfile()
        {
            const string protSequence =
                "AIPQSVEGQSIPSLAPMLERTTPAVVSVAVSGTHVSKQRVPDVFRYFFGPNAPQEQVQERPFRGLGSGVIIDADKGYIVTNNHVIDGADDIQVGLHDGREVKAKLIGTDSESDIALLQIEAKNLVAIKTSDSDELRVGDFAVAIGNPFGLGQTVTSGIVSALGRSGLGIEMLENFIQTDAAINSGNSGGALVNLKGELIGINTAIVAPNGGNVGIGFAIPANMVKNLIAQIAEHGEVRRGVLGIAGRDLDSQLAQGFGLDTQHGGFVNEVSAGSAAEKAGIKAGDIIVSVDGRAIKSFQELRAKVATMGAGAKVELGLIRDGDKKTVNVTLGEANQTTEKAAGAVHPMLQGASLENASKGVEITDVAQGSPAAMSGLQKGDLIVGINRTAVKDLKSLKELLKDQEGAVALKIVRGKSMLYLVLR";
            const string annotation = "_." + protSequence + "._";
            var seqGraph = SequenceGraph.CreateGraph(new AminoAcidSet(), AminoAcid.ProteinNTerm, protSequence, AminoAcid.ProteinCTerm);
            if (seqGraph == null) return;
            seqGraph.SetSink(0);
            var neutral = seqGraph.GetSinkSequenceCompositionWithH2O() - Composition.Hydrogen;
            //Console.WriteLine(neutral);

            for (var charge = 22; charge <= 60; charge++)
            {
                var ion = new Ion(neutral, charge);
                Console.WriteLine("{0}\t{1}", charge, ion.GetMostAbundantIsotopeMz());
            }

            var ion27 = new Ion(neutral, 29);
            var isotopes = ion27.GetIsotopes(0.1); 
            foreach (var isotope in isotopes)
            {
                Console.WriteLine("{0}\t{1}", ion27.GetIsotopeMz(isotope.Index), isotope.Ratio);
            }                
        }

        [Test]
        public void TestGeneringAllXics()
        {
            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath, MassSpecDataType.XCaliburRun, 0.0, 0.0);
            Assert.True(run != null);
            var comparer = new MzComparerWithBinning(27);
            const double minMz = 600.0; // 600.0
            const double maxMz = 2000.0;    // 2000.0
            var minBinNum = comparer.GetBinNumber(minMz);
            var maxBinNum = comparer.GetBinNumber(maxMz);
            Console.WriteLine("NumBins: " + (maxBinNum - minBinNum));
            var sw = new Stopwatch();
            sw.Start();
            var numBinsProcessed = 0;
            for (var binNum = minBinNum; binNum <= maxBinNum; binNum++)
            {
//                if (binNum < 33880544) continue;
//                Console.WriteLine(binNum);
                var mzStart = comparer.GetMzStart(binNum);
                var mzEnd = comparer.GetMzEnd(binNum);
                //var between = 0.4*mz + 0.6*mzNext;
                //Console.WriteLine(comparer.GetBinNumber(mz) + " " + comparer.GetBinNumber(between) + " " + comparer.GetBinNumber(mzNext));
                //Assert.True(binNum == comparer.GetBinNumber(mzNextMinusEpsilon));
                //run.GetFullPrecursorIonExtractedIonChromatogram(mz, mzNext);
                run.GetPrecursorExtractedIonChromatogram(mzStart, mzEnd);
                //if(++numBinsProcessed % 1000 == 0) Console.WriteLine(numBinsProcessed);
            }
            sw.Stop();
            var sec = sw.ElapsedTicks / (double)Stopwatch.Frequency;
            Console.WriteLine(@"{0:f4} sec", sec);
        }

        [Test]
        public void TestGeneratingXicsOfAllCharges()
        {
            //var run = InMemoryLcMsRun.GetLcMsRun(TestRawFilePath, MassSpecDataType.XCaliburRun, 1.4826, 0.0);
            var run = PbfLcMsRun.GetLcMsRun(TestRawFilePath, MassSpecDataType.XCaliburRun, 0.0, 0.0);
            var comparer = new MzComparerWithBinning(27);
            const string protSequence =
                "AIPQSVEGQSIPSLAPMLERTTPAVVSVAVSGTHVSKQRVPDVFRYFFGPNAPQEQVQERPFRGLGSGVIIDADKGYIVTNNHVIDGADDIQVGLHDGREVKAKLIGTDSESDIALLQIEAKNLVAIKTSDSDELRVGDFAVAIGNPFGLGQTVTSGIVSALGRSGLGIEMLENFIQTDAAINSGNSGGALVNLKGELIGINTAIVAPNGGNVGIGFAIPANMVKNLIAQIAEHGEVRRGVLGIAGRDLDSQLAQGFGLDTQHGGFVNEVSAGSAAEKAGIKAGDIIVSVDGRAIKSFQELRAKVATMGAGAKVELGLIRDGDKKTVNVTLGEANQTTEKAAGAVHPMLQGASLENASKGVEITDVAQGSPAAMSGLQKGDLIVGINRTAVKDLKSLKELLKDQEGAVALKIVRGKSMLYLVLR";
            const string annotation = "_." + protSequence + "._";
            var seqGraph = SequenceGraph.CreateGraph(new AminoAcidSet(), AminoAcid.ProteinNTerm, protSequence, AminoAcid.ProteinCTerm);
            if (seqGraph == null) return;
            seqGraph.SetSink(0);
            var neutral = seqGraph.GetSinkSequenceCompositionWithH2O() - Composition.Hydrogen;
            var proteinMass = neutral.Mass;
            var isoEnv = Averagine.GetIsotopomerEnvelope(proteinMass);

            Console.WriteLine("Charge\t" + string.Join("\t", run.GetScanNumbers(1)));
            const int minCharge = 2;
            const int maxCharge = 60;
            for (var charge = minCharge; charge <= maxCharge; charge++)
            {
                var ion = new Ion(neutral, charge);
                var mostAbundantIsotopeMz = ion.GetIsotopeMz(isoEnv.MostAbundantIsotopeIndex);
                //var secondMostAbundantIsotopeMz = ion.GetIsotopeMz(isoEnv.MostAbundantIsotopeIndex + 1);
                var binNum = comparer.GetBinNumber(mostAbundantIsotopeMz);
                var mzStart = comparer.GetMzStart(binNum);
                var mzEnd = comparer.GetMzEnd(binNum);

                var xic = run.GetFullPrecursorIonExtractedIonChromatogram(mzStart, mzEnd);
                Console.Write(charge+"\t");
                Console.WriteLine(string.Join("\t", xic.Select(p => p.Intensity)));
            }
        }

        [Test]
        public void TestAbpSumMs1Spectra()
        {
            const string specFilePath = @"C:\cygwin\home\kims336\Data\TopDownQCShew\raw\QC_ShewIntact_2ug_3k_CID_4Apr14_Bane_PL011402.raw";

            const int minScanNum = 5657;
            const int maxScanNum = 5699;

            var run = PbfLcMsRun.GetLcMsRun(specFilePath);
            if (run == null) return;
            var summedSpec = run.GetSummedMs1Spectrum(minScanNum, maxScanNum);
            var peakList = summedSpec.GetPeakListWithin(1180.0, 1192.0);
            var filteredPeakList = new List<Peak>();
            PeakListUtils.FilterNoise(peakList, ref filteredPeakList);
            new Spectrum(filteredPeakList, 0).Display();
        }

        [Test]
        public void TestIsoProfile()
        {
            const string sequence = "MWYMISAQDVENSLEKRLAARPAHLARLQELADEGRLLVAGPHPAIDSENPGDAGFSGSLVVADFDSLATAQAWADADPYFAAGVYQSVVVKPFKRVLP";
            var aaSet = new AminoAcidSet();
            var comp = aaSet.GetComposition(sequence) + Composition.H2O;
            var ion = new Ion(comp, 9);
            foreach (var i in ion.GetIsotopes(0.1))
            {
                Console.WriteLine(ion.GetIsotopeMz(i.Index)+"\t"+i.Ratio);
            }
        }
    }
}
