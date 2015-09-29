﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.Database;
using InformedProteomics.Backend.MassFeature;
using InformedProteomics.Backend.MassSpecData;
using InformedProteomics.Backend.Utils;
using InformedProteomics.TopDown.Execution;
using MathNet.Numerics.Statistics;
using NUnit.Framework;


namespace InformedProteomics.Test.TopDownAnalysis
{
    public class AnalysisCompRef
    {
        //public const string OutFilePath = @"\\protoapps\UserData\Jungkap\CompRef\aligned\aligned_features.tsv";
        public const string Ms1FtFolder = @"\\protoapps\UserData\Jungkap\CompRef\ms1ft";
        public const string MsPfFolder = @"\\protoapps\UserData\Jungkap\CompRef\MSPF";
        public const string RawFolder = @"\\protoapps\UserData\Jungkap\CompRef\raw";
        
        internal class CompRefFeatureComparer : INodeComparer<LcMsFeature>
        {
            public CompRefFeatureComparer(Tolerance tolerance = null)
            {
                _tolerance = tolerance ?? new Tolerance(10);
            }

            public bool SameCluster(LcMsFeature f1, LcMsFeature f2)
            {
                if (f1.DataSetId == f2.DataSetId) return false;
                // tolerant in mass dimension?

                var massTol = Math.Min(_tolerance.GetToleranceAsTh(f1.Mass), _tolerance.GetToleranceAsTh(f2.Mass));
                if (Math.Abs(f1.Mass - f2.Mass) > massTol) return false;

                //if (!f1.CoElutedByNet(f2, 0.004)) return false; //e.g) 200*0.001 = 0.2 min = 30 sec
                if (f1.ProteinSpectrumMatches.Count > 0 && f2.ProteinSpectrumMatches.Count > 0)
                {
                    return f1.CoElutedByNet(f2, 0.03) && f1.ProteinSpectrumMatches.ShareProteinId(f2.ProteinSpectrumMatches);
                }

                return f1.CoElutedByNet(f2, 0.01); 
            }

            private readonly Tolerance _tolerance;
        }


        [Test]
        public void TestFeatureAlignment()
        {
            const string outFilePath = @"\\protoapps\UserData\Jungkap\CompRef\aligned\promex_crosstab_temp.tsv";
            const string outFolder = @"\\protoapps\UserData\Jungkap\CompRef\aligned";
            var runLabels = new string[] {"32A", "32B", "32C", "32D", "32E", "32F", "32G", "33A", "33B", "33C", "33D", "33E", "33F", "33G"};
            var nDataset = runLabels.Length;
            //CPTAC_Intact_CR32A_24Aug15_Bane_15-02-06-RZ
            var prsmReader = new ProteinSpectrumMatchReader();
            var tolerance = new Tolerance(10);
            var alignment = new LcMsFeatureAlignment(new CompRefFeatureComparer(tolerance));

            for (var i = 0; i < nDataset; i++)
            {
                var rawFile = string.Format(@"{0}\CPTAC_Intact_CR{1}_24Aug15_Bane_15-02-06-RZ.pbf", RawFolder, runLabels[i]);
                var mspFile = string.Format(@"{0}\CPTAC_Intact_CR{1}_24Aug15_Bane_15-02-06-RZ_IcTda.tsv", MsPfFolder, runLabels[i]);
                var ms1FtFile = string.Format(@"{0}\CPTAC_Intact_CR{1}_24Aug15_Bane_15-02-06-RZ.ms1ft", Ms1FtFolder, runLabels[i]);

                var run = PbfLcMsRun.GetLcMsRun(rawFile);
                var prsmList = prsmReader.LoadIdentificationResult(mspFile, ProteinSpectrumMatch.SearchTool.MsPathFinder);
                var features = LcMsFeatureAlignment.LoadProMexResult(i, ms1FtFile, run);

                for (var j = 0; j < prsmList.Count; j++)
                {
                    var match = prsmList[j];
                    match.ProteinId = match.ProteinName;
                }

                // tag features by PrSMs
                for (var j = 0; j < features.Count; j++)
                {
                    //features[j].ProteinSpectrumMatches = new ProteinSpectrumMatchSet(i);
                    var massTol = tolerance.GetToleranceAsTh(features[j].Mass);
                    foreach (var match in prsmList)
                    {
                        if (features[j].MinScanNum < match.ScanNum && match.ScanNum < features[j].MaxScanNum && Math.Abs(features[j].Mass - match.Mass) < massTol)
                        {
                            features[j].ProteinSpectrumMatches.Add(match);
                        }
                    }
                }

                alignment.AddDataSet(i, features, run);                
            }

            alignment.AlignFeatures();
            
            Console.WriteLine("{0} alignments ", alignment.CountAlignedFeatures);
            /*
            for (var i = 0; i < nDataset; i++)
            {
                alignment.FillMissingFeatures(i);
                Console.WriteLine("{0} has been processed", runLabels[i]);
            }
            */
            OutputCrossTabWithId(outFilePath, alignment, runLabels);
        }

        public void OutputCrossTabWithId(string outputFilePath, LcMsFeatureAlignment alignment, string[] runLabels)
        {
            var nDataset = runLabels.Length;
            var writer = new StreamWriter(outputFilePath);
            
            writer.Write("MonoMass");
            writer.Write("\t");
            writer.Write("MinElutionTime");
            writer.Write("\t");
            writer.Write("MaxElutionTime");

            foreach (var dataName in runLabels)
            {
                writer.Write("\t");
                writer.Write(dataName + "_Abundance");
            }

            foreach (var dataName in runLabels)
            {
                writer.Write("\t");
                writer.Write(dataName + "_Ms1Score");
            }
            
            writer.Write("\t");
            writer.Write("Pre");
            writer.Write("\t");
            writer.Write("Sequence");
            writer.Write("\t");
            writer.Write("Post");
            writer.Write("\t");
            writer.Write("Modifications");
            writer.Write("\t");
            writer.Write("ProteinName");
            writer.Write("\t");
            writer.Write("ProteinDesc");
            writer.Write("\t");
            writer.Write("ProteinLength");
            writer.Write("\t");
            writer.Write("Start");
            writer.Write("\t");
            writer.Write("End");
            foreach (var dataName in runLabels)
            {
                writer.Write("\t");
                writer.Write(dataName + "_SpectraCount");
            }
            writer.Write("\n");

            var alignedFeatureList = alignment.GetAlignedFeatures();
            for (var j = 0; j < alignedFeatureList.Count; j++)
            {
                var features = alignedFeatureList[j];
                var mass = features.Where(f => f != null).Select(f => f.Mass).Median();
                var minElutionTime = features.Where(f => f != null).Select(f => f.MinElutionTime).Median();
                var maxElutionTime = features.Where(f => f != null).Select(f => f.MaxElutionTime).Median();
                writer.Write(mass);
                writer.Write("\t");
                writer.Write(minElutionTime);
                writer.Write("\t");
                writer.Write(maxElutionTime);

                for (var i = 0; i < nDataset; i++)
                {
                    writer.Write("\t");
                    writer.Write(features[i] == null ? 0 : features[i].Abundance);
                }

                for (var i = 0; i < nDataset; i++)
                {
                    writer.Write("\t");
                    writer.Write(features[i] == null ? 0 : features[i].Score);
                }

                var prsm = (from f in features
                    where f != null && f.ProteinSpectrumMatches != null && f.ProteinSpectrumMatches.Count > 0
                    select f.ProteinSpectrumMatches[0]).FirstOrDefault();

                if (prsm == null)
                {
                    for (var k = 0; k < 9; k++)
                    {
                        writer.Write("\t");
                        writer.Write(" ");
                    }
                }
                else
                {
                    writer.Write("\t");
                    writer.Write(prsm.Pre);
                    writer.Write("\t");
                    writer.Write(prsm.Sequence);
                    writer.Write("\t");
                    writer.Write(prsm.Post);
                    writer.Write("\t");
                    writer.Write(prsm.Modifications);
                    writer.Write("\t");
                    writer.Write(prsm.ProteinName);
                    writer.Write("\t");
                    writer.Write(prsm.ProteinDesc);
                    writer.Write("\t");
                    writer.Write(prsm.ProteinLength);
                    writer.Write("\t");
                    writer.Write(prsm.FirstResidue);
                    writer.Write("\t");
                    writer.Write(prsm.LastResidue);
                }
                
                // spectral count from ms2
                for (var i = 0; i < nDataset; i++)
                {
                    writer.Write("\t");
                    writer.Write(features[i] == null ? 0 : features[i].ProteinSpectrumMatches.Count);
                }
                writer.Write("\n");
            }

            writer.Close();
            
        }

    }
}