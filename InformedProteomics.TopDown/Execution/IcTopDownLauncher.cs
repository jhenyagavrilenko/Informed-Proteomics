﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InformedProteomics.Backend.Data.Biology;
using InformedProteomics.Backend.Data.Composition;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.Database;
using InformedProteomics.Backend.MassFeature;
using InformedProteomics.Backend.MassSpecData;
using InformedProteomics.Backend.Results;
using InformedProteomics.Backend.Utils;
using InformedProteomics.Scoring.GeneratingFunction;
using InformedProteomics.Scoring.TopDown;
using InformedProteomics.TopDown.Scoring;
using InformedProteomics.TopDown.TagBasedSearch;
using PSI_Interface.CV;
using PSI_Interface.IdentData;
using PSI_Interface.IdentData.IdentDataObjs;
using PSI_Interface.IdentData.mzIdentML;
using TopDownTrainer;

namespace InformedProteomics.TopDown.Execution
{
    public class IcTopDownLauncher
    {
        //public const int NumMatchesPerSpectrum = 1;
        public const string TargetFileNameEnding = "_IcTarget.tsv";
        public const string DecoyFileNameEnding = "_IcDecoy.tsv";
        public const string TdaFileNameEnding = "_IcTda.tsv";
        public const string MzidFileNameEnding = ".mzid";

        public IcTopDownLauncher(
            string specFilePath,
            string dbFilePath,
            string outputDir,
            AminoAcidSet aaSet,
            string featureFilePath = null)
        {
            ErrorMessage = string.Empty;

            SpecFilePath = specFilePath;
            DatabaseFilePath = dbFilePath;
            AminoAcidSet = aaSet;
            OutputDir = outputDir;

            FeatureFilePath = featureFilePath;

            MinSequenceLength = 21;
            MaxSequenceLength = 300;
            MaxNumNTermCleavages = 1;
            MaxNumCTermCleavages = 0;
            MinPrecursorIonCharge = 2;
            MaxPrecursorIonCharge = 60;
            MinProductIonCharge = 1;
            MaxProductIonCharge = 20;
            MinSequenceMass = 2000.0;
            MaxSequenceMass = 50000.0;
            PrecursorIonTolerance = new Tolerance(10);
            ProductIonTolerance = new Tolerance(10);
            RunTargetDecoyAnalysis = DatabaseSearchMode.Both;
            SearchMode = InternalCleavageType.SingleInternalCleavage;
            MaxNumThreads = 4;
            ScanNumbers = null;
            NumMatchesPerSpectrum = 3;
            TagBasedSearch = true;
        }

        public string ErrorMessage { get; private set; }
        public string SpecFilePath { get; private set; }
        public string DatabaseFilePath { get; private set; }
        public string OutputDir { get; private set; }
        public AminoAcidSet AminoAcidSet { get; private set; }
        public string FeatureFilePath { get; set; }

        /// <remarks>default 21</remarks>
        public int MinSequenceLength { get; set; }

        /// <remarks>default 300</remarks>
        public int MaxSequenceLength { get; set; }

        /// <remarks>default 1</remarks>
        public int MaxNumNTermCleavages { get; set; }

        /// <remarks>default 0</remarks>
        public int MaxNumCTermCleavages { get; set; }

        /// <remarks>default 2</remarks>
        public int MinPrecursorIonCharge { get; set; }

        /// <remarks>default 60</remarks>
        public int MaxPrecursorIonCharge { get; set; }

        /// <remarks>default 2000</remarks>
        public double MinSequenceMass { get; set; }

        /// <remarks>default 50000</remarks>
        public double MaxSequenceMass { get; set; }

        /// <remarks>default 1</remarks>
        public int MinProductIonCharge { get; set; }

        /// <remarks>default 20</remarks>
        public int MaxProductIonCharge { get; set; }

        /// <remarks>default 10 ppm</remarks>
        public Tolerance PrecursorIonTolerance { get; set; }

        /// <remarks>default 10 ppm</remarks>
        public Tolerance ProductIonTolerance { get; set; }

        /// <remarks>default true
        /// true: target and decoy, false: target only, null: decoy only</remarks>
        public bool? RunTargetDecoyAnalysisBool
        {
            get
            {
                if (RunTargetDecoyAnalysis == DatabaseSearchMode.Both)
                    return true;
                if (RunTargetDecoyAnalysis == DatabaseSearchMode.Decoy)
                    return null;
                //(Tda2 == DatabaseSearchMode.Target)
                return false;
            }
            set
            {
                if (value == null)
                {
                    RunTargetDecoyAnalysis = DatabaseSearchMode.Decoy;
                }
                else if (value.Value)
                {
                    RunTargetDecoyAnalysis = DatabaseSearchMode.Both;
                }
                else
                {
                    RunTargetDecoyAnalysis = DatabaseSearchMode.Target;
                }
            }
        }

        /// <remarks>default Both</remarks>
        public DatabaseSearchMode RunTargetDecoyAnalysis { get; set; }

        public bool TagBasedSearch { get; set; }

        /// <summary>
        /// Specific MS2 scan numbers to process
        /// </summary>
        public IEnumerable<int> ScanNumbers { get; set; }

        /// <remarks>default 3</remarks>
        public int NumMatchesPerSpectrum { get; set; }

        /// <remarks>default 4</remarks>
        public int MaxNumThreads { get; set; }

        /// <remarks>default 10 ppm</remarks>
        public double PrecursorIonTolerancePpm
        {
            get { return PrecursorIonTolerance.GetValue(); }
            set { PrecursorIonTolerance = new Tolerance(value);}
        }

        /// <remarks>default 10 ppm</remarks>
        public double ProductIonTolerancePpm
        {
            get { return ProductIonTolerance.GetValue(); }
            set { ProductIonTolerance = new Tolerance(value); }
        }

        /// <summary>
        /// 0: all internal sequences,
        /// 1: #NCleavages &lt;= Max OR Cleavages &lt;= Max (Default)
        /// 2: 1: #NCleavages &lt;= Max AND Cleavages &lt;= Max
        /// </summary>
        /// <remarks>default 1</remarks>
        public int SearchModeInt
        {
            get
            {
                if (SearchMode == InternalCleavageType.MultipleInternalCleavages)
                    return 0;
                if (SearchMode == InternalCleavageType.SingleInternalCleavage)
                    return 1;
                return 2;
            }
            set
            {
                if (value == 0)
                {
                    SearchMode = InternalCleavageType.MultipleInternalCleavages;
                }
                else if (value == 1)
                {
                    SearchMode = InternalCleavageType.SingleInternalCleavage;
                }
                else
                {
                    SearchMode = InternalCleavageType.NoInternalCleavage;
                }
            }
        }

        /// <remarks>default SingleInternalCleavage</remarks>
        public InternalCleavageType SearchMode { get; set; }

        private LcMsRun _run;
        private CompositeScorerFactory _ms2ScorerFactory2;
        private IFragmentScorerFactory fragmentScorerFactory;
        private IMassBinning _massBinComparer;
        private ScanBasedTagSearchEngine _tagSearchEngine;
        private double[] _isolationWindowTargetMz; // spec.IsolationWindow.IsolationWindowTargetMz
        private List<int> _ms2ScanNums;

        public bool RunSearch(double corrThreshold = 0.7, CancellationToken? cancellationToken = null, IProgress<ProgressData> progress = null)
        {
            // Get the Normalized spec file/folder path
            SpecFilePath = MassSpecDataReaderFactory.NormalizeDatasetPath(SpecFilePath);

            var prog = new Progress<ProgressData>();
            var progData = new ProgressData(progress);
            if (progress != null)
            {
                prog = new Progress<ProgressData>(p =>
                {
                    progData.Status = p.Status;
                    progData.StatusInternal = p.StatusInternal;
                    progData.Report(p.Percent);
                });
            }

            var sw = new Stopwatch();
            var swAll = new Stopwatch();
            swAll.Start();
            ErrorMessage = string.Empty;

            if (string.Equals(Path.GetExtension(SpecFilePath), ".pbf", StringComparison.InvariantCultureIgnoreCase))
                Console.Write(@"Reading pbf file...");
            else
                Console.Write(@"Reading raw file...");

            progData.Status = "Reading spectra file";
            progData.StepRange(5.0);
            sw.Start();

            _run = PbfLcMsRun.GetLcMsRun(SpecFilePath, 0, 0, prog);

            var minMs1Scan = int.MinValue;
            var maxMs1Scan = int.MaxValue;

            // Retrieve the list of MS2 scans
            _ms2ScanNums = _run.GetScanNumbers(2).ToList();

            if (ScanNumbers != null && ScanNumbers.Any())
            {
                // Filter the MS2 scans using ScanNumbers
                _ms2ScanNums = _ms2ScanNums.Intersect(ScanNumbers).ToList();

                minMs1Scan = _ms2ScanNums.Min() - 100;
                maxMs1Scan = _ms2ScanNums.Max() + 100;
            }

            _isolationWindowTargetMz = new double[_run.MaxLcScan + 1];
            foreach (var ms2Scan in _ms2ScanNums)
            {
                var ms2Spec = _run.GetSpectrum(ms2Scan) as ProductSpectrum;
                if (ms2Spec == null) continue;
                _isolationWindowTargetMz[ms2Scan] = ms2Spec.IsolationWindow.IsolationWindowTargetMz;
            }

            sw.Stop();
            Console.WriteLine(@"Elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds);

            progData.StepRange(10);
            progData.Status = "Reading Fasta File";
            Console.WriteLine(progData.Status);

            // Target database
            var targetDb = new FastaDatabase(DatabaseFilePath);
            targetDb.Read();

            progData.StepRange(20.0);
            ISequenceFilter ms1Filter;
            if (this.ScanNumbers != null && this.ScanNumbers.Any())
            {
                ms1Filter = new SelectedMsMsFilter(this.ScanNumbers);
            }
            else if (string.IsNullOrWhiteSpace(FeatureFilePath))
            {
                // Checks whether SpecFileName.ms1ft exists
                var ms1FtFilePath = MassSpecDataReaderFactory.ChangeExtension(SpecFilePath, LcMsFeatureFinderLauncher.FileExtension);
                if (!File.Exists(ms1FtFilePath))
                {
                    Console.WriteLine(@"Running ProMex...");
                    sw.Reset();
                    sw.Start();
                    var param = new LcMsFeatureFinderInputParameter
                    {
                        InputPath = SpecFilePath,
                        MinSearchMass = MinSequenceMass,
                        MaxSearchMass = MaxSequenceMass,
                        MinSearchCharge = MinPrecursorIonCharge,
                        MaxSearchCharge = MaxPrecursorIonCharge,
                        CsvOutput = false,
                        ScoreReport = false,
                        LikelihoodScoreThreshold = -10
                    };
                    var featureFinder = new LcMsFeatureFinderLauncher(param);
                    featureFinder.Run();
                }
                sw.Reset();
                sw.Start();
                Console.Write(@"Reading ProMex results...");
                ms1Filter = new Ms1FtFilter(_run, PrecursorIonTolerance, ms1FtFilePath, -10);
            }
            else
            {
                sw.Reset();
                sw.Start();
                var extension = Path.GetExtension(FeatureFilePath);
                if (extension.ToLower().Equals(".csv"))
                {
                    Console.Write(@"Reading ICR2LS/Decon2LS results...");
                    ms1Filter = new IsosFilter(_run, PrecursorIonTolerance, FeatureFilePath);
                }
                else if (extension.ToLower().Equals(".ms1ft"))
                {
                    Console.Write(@"Reading ProMex results...");
                    ms1Filter = new Ms1FtFilter(_run, PrecursorIonTolerance, FeatureFilePath, -10);
                }
                else if (extension.ToLower().Equals(".msalign"))
                {
                    Console.Write(@"Reading MS-Align+ results...");
                    ms1Filter = new MsDeconvFilter(_run, PrecursorIonTolerance, FeatureFilePath);
                }
                else ms1Filter = null; //new Ms1FeatureMatrix(_run);
            }

            sw.Stop();
            Console.WriteLine(@"Elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds);

            // pre-generate deconvoluted spectra for scoring
            _massBinComparer = new FilteredProteinMassBinning(AminoAcidSet, MaxSequenceMass+1000);

            this.fragmentScorerFactory = new CompositionScorerFactory(this._run, true);
            _ms2ScorerFactory2 = new CompositeScorerFactory(_run, _massBinComparer, AminoAcidSet,
                                                               MinProductIonCharge, MaxProductIonCharge, ProductIonTolerance);
            sw.Reset();
            Console.WriteLine(@"Generating deconvoluted spectra for MS/MS spectra...");
            sw.Start();
            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };
            Parallel.ForEach(_ms2ScanNums, pfeOptions, ms2ScanNum =>
            {
                _ms2ScorerFactory2.DeconvonluteProductSpectrum(ms2ScanNum);
            });
            sw.Stop();
            Console.WriteLine(@"Elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds);

            // Generate sequence tags for all MS/MS spectra
            if (TagBasedSearch)
            {
                progData.StepRange(25.0);
                progData.Status = "Generating Sequence Tags";

                sw.Reset();
                Console.WriteLine(@"Generating sequence tags for MS/MS spectra...");
                sw.Start();
                var seqTagGen = GetSequenceTagGenerator();
                _tagMs2ScanNum = seqTagGen.GetMs2ScanNumsContainingTags().ToArray();
                sw.Stop();
                Console.WriteLine(@"Elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds);

                var peakMatrix = new LcMsPeakMatrix(_run, ms1Filter, minMs1Scan: minMs1Scan, maxMs1Scan: maxMs1Scan);
                _tagSearchEngine = new ScanBasedTagSearchEngine(
                    _run, seqTagGen, peakMatrix, targetDb, ProductIonTolerance, AminoAcidSet,
                    _ms2ScorerFactory2, ScanBasedTagSearchEngine.DefaultMinMatchedTagLength,
                    MaxSequenceMass, MinProductIonCharge, MaxProductIonCharge);
            }

            var specFileName = MassSpecDataReaderFactory.RemoveExtension(Path.GetFileName(SpecFilePath));
            var targetOutputFilePath = Path.Combine(OutputDir, specFileName + TargetFileNameEnding);
            var decoyOutputFilePath = Path.Combine(OutputDir, specFileName + DecoyFileNameEnding);
            var tdaOutputFilePath = Path.Combine(OutputDir, specFileName + TdaFileNameEnding);
            var mzidOutputFilePath = Path.Combine(OutputDir, specFileName + MzidFileNameEnding);

            progData.StepRange(60.0);
            progData.Status = "Running Target search";
            List<DatabaseSearchResultData> targetSearchResults = null;

            if (RunTargetDecoyAnalysis.HasFlag(DatabaseSearchMode.Target) && !File.Exists(targetOutputFilePath))
            {
                targetSearchResults = RunDatabaseSearch(targetDb, targetOutputFilePath, ms1Filter, "target", prog);
            }
            else if (File.Exists(targetOutputFilePath))
            {
                Console.WriteLine(@"Target results file '{0}' exists; skipping target search.", targetOutputFilePath);
            }

            progData.StepRange(95.0); // total to 95%
            progData.Status = "Running Decoy search";
            List<DatabaseSearchResultData> decoySearchResults = null;

            if (RunTargetDecoyAnalysis.HasFlag(DatabaseSearchMode.Decoy) && !File.Exists(decoyOutputFilePath))
            {
                var decoyDb = targetDb.Decoy(null, true);
                decoySearchResults = RunDatabaseSearch(decoyDb, decoyOutputFilePath, ms1Filter, "decoy", prog);
            }
            else if (File.Exists(decoyOutputFilePath))
            {
                Console.WriteLine(@"Decoy results file '{0}' exists; skipping decoy search.", decoyOutputFilePath);
            }

            progData.StepRange(100.0);
            progData.Status = "Writing combined results file";
            if (RunTargetDecoyAnalysis.HasFlag(DatabaseSearchMode.Both))
            {
                // Add "Qvalue" and "PepQValue"
                FdrCalculator fdrCalculator;
                if (targetSearchResults == null && decoySearchResults == null)
                {
                    // Objects not populated, try to read in the files.
                    fdrCalculator = new FdrCalculator(targetOutputFilePath, decoyOutputFilePath);
                }
                else if (targetSearchResults == null)
                {
                    // Target search skipped, read in the result file
                    targetSearchResults = DatabaseSearchResultData.ReadResultsFromFile(targetOutputFilePath);
                    fdrCalculator = new FdrCalculator(targetSearchResults, decoySearchResults);
                }
                else if (decoySearchResults == null)
                {
                    // Decoy search skipped, read in the result file
                    decoySearchResults = DatabaseSearchResultData.ReadResultsFromFile(decoyOutputFilePath);
                    fdrCalculator = new FdrCalculator(targetSearchResults, decoySearchResults);
                }
                else
                {
                    // Just use the objects
                    fdrCalculator = new FdrCalculator(targetSearchResults, decoySearchResults);
                }

                if (fdrCalculator.HasError())
                {
                    ErrorMessage = fdrCalculator.ErrorMessage;
                    Console.WriteLine(@"Error computing FDR: " + fdrCalculator.ErrorMessage);
                    return false;
                }

                fdrCalculator.WriteTo(tdaOutputFilePath);
                WriteResultsToMzid(fdrCalculator.FilteredResults, mzidOutputFilePath, targetDb, _run);
            }
            progData.Report(100.0);

            Console.WriteLine(@"Done.");
            swAll.Stop();
            Console.WriteLine(@"Total elapsed time for search: {0:f1} sec ({1:f2} min)", swAll.Elapsed.TotalSeconds, swAll.Elapsed.TotalMinutes);

            return true;
        }

        private List<DatabaseSearchResultData> RunDatabaseSearch(FastaDatabase searchDb, string outputFilePath, ISequenceFilter ms1Filter, string searchModeString, IProgress<ProgressData> progress)
        {
            var progressData = new ProgressData(progress);
            var sw = new Stopwatch();
            var searchModeStringCap = char.ToUpper(searchModeString[0]) + searchModeString.Substring(1);

            sw.Reset();
            sw.Start();
            Console.WriteLine(@"Reading the {0} database...", searchModeString);
            searchDb.Read();
            sw.Stop();
            Console.WriteLine(@"Elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds);

            var matches = new SortedSet<DatabaseSequenceSpectrumMatch>[_run.MaxLcScan + 1];
            progressData.StepRange(50);
            if (TagBasedSearch)
            {
                sw.Reset();
                Console.WriteLine(@"Tag-based searching the {0} database", searchModeString);
                sw.Start();
                var progTag = new Progress<ProgressData>(p =>
                {
                    progressData.StatusInternal = p.Status;
                    progressData.Report(p.Percent);
                });
                RunTagBasedSearch(matches, searchDb, null, progTag);
                Console.WriteLine(@"{1} database tag-based search elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds, searchModeStringCap);
            }
            progressData.StepRange(100);

            sw.Reset();
            Console.WriteLine(@"Searching the {0} database", searchModeString);
            sw.Start();
            var prog = new Progress<ProgressData>(p =>
            {
                progressData.StatusInternal = p.Status;
                progressData.Report(p.Percent);
            });
            RunSearch(matches, searchDb, ms1Filter, null, prog);
            Console.WriteLine(@"{1} database search elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds, searchModeStringCap);

            // calculate spectral e-value using generating function
            sw.Reset();
            Console.WriteLine(@"Calculating spectral E-values for {0}-spectrum matches", searchModeString);
            sw.Start();
            var bestMatches = RunGeneratingFunction(matches);
            var results = WriteResultsToFile(bestMatches, outputFilePath, searchDb);
            sw.Stop();
            Console.WriteLine(@"{1}-spectrum match E-value calculation elapsed Time: {0:f1} sec", sw.Elapsed.TotalSeconds, searchModeStringCap);
            return results;
        }

        private int[] _tagMs2ScanNum;

        private IEnumerable<AnnotationAndOffset> GetAnnotationsAndOffsets(FastaDatabase database, out long estimatedProteins, CancellationToken? cancellationToken = null)
        {
            var indexedDb = new IndexedDatabase(database);
            indexedDb.Read();
            estimatedProteins = indexedDb.EstimateTotalPeptides(SearchMode, MinSequenceLength, MaxSequenceLength, MaxNumNTermCleavages, MaxNumCTermCleavages);
            IEnumerable<AnnotationAndOffset> annotationsAndOffsets;
            if (SearchMode == InternalCleavageType.MultipleInternalCleavages)
            {
                //annotationsAndOffsets = indexedDb.AnnotationsAndOffsetsNoEnzyme(MinSequenceLength, MaxSequenceLength);
                annotationsAndOffsets = indexedDb.AnnotationsAndOffsetsNoEnzymeParallel(MinSequenceLength, MaxSequenceLength, MaxNumThreads, cancellationToken);
            }
            else if (SearchMode == InternalCleavageType.NoInternalCleavage)
            {
                annotationsAndOffsets = indexedDb.IntactSequenceAnnotationsAndOffsets(MinSequenceLength, MaxSequenceLength, MaxNumCTermCleavages);
            }
            else
            {
                annotationsAndOffsets = indexedDb
                    .SequenceAnnotationsAndOffsetsWithNtermOrCtermCleavageNoLargerThan(
                        MinSequenceLength, MaxSequenceLength, MaxNumNTermCleavages, MaxNumCTermCleavages);
            }

            return annotationsAndOffsets;
        }

        private void RunTagBasedSearch(SortedSet<DatabaseSequenceSpectrumMatch>[] matches, FastaDatabase db,
                                        CancellationToken? cancellationToken = null, IProgress<ProgressData> progress = null)
        {
            _tagSearchEngine.SetDatabase(db);

            var progData = new ProgressData(progress)
            {
                Status = "Tag-based Searching for matches"
            };

            var sw = new Stopwatch();

            long estimatedProteins = _tagMs2ScanNum.Length;
            Console.WriteLine(@"Number of spectra containing sequence tags: " + estimatedProteins);
            var numProteins = 0;
            var lastUpdate = DateTime.MinValue; // Force original update of 0%

            sw.Reset();
            sw.Start();

            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };

            Parallel.ForEach(_tagMs2ScanNum, pfeOptions, ms2ScanNum =>
            {
                var tagSeqMatches = _tagSearchEngine.RunSearch(ms2ScanNum);

                foreach (var tagSequenceMatch in tagSeqMatches)
                {
                    var offset = _tagSearchEngine.FastaDatabase.GetOffset(tagSequenceMatch.ProteinName);
                    if (offset == null) continue;

                    var sequence = tagSequenceMatch.Sequence;
                    var numNTermCleavages = tagSequenceMatch.TagMatch.StartIndex;

                    var seqObj = Sequence.CreateSequence(sequence, tagSequenceMatch.TagMatch.ModificationText, AminoAcidSet);
                    var precursorIon = new Ion(seqObj.Composition + Composition.H2O, tagSequenceMatch.TagMatch.Charge);

                    var prsm = new DatabaseSequenceSpectrumMatch(sequence, tagSequenceMatch.Pre, tagSequenceMatch.Post,
                                                                 ms2ScanNum, (long)offset, numNTermCleavages,
                                                                 tagSequenceMatch.TagMatch.Modifications,
                                                                 precursorIon, tagSequenceMatch.TagMatch.Score, db.IsDecoy)
                    {
                        ModificationText = tagSequenceMatch.TagMatch.ModificationText,
                    };

                    AddMatch(matches, ms2ScanNum, prsm);
                }

                SearchProgressReport(ref numProteins, ref lastUpdate, estimatedProteins, sw, progData, "spectra");
            });

            Console.WriteLine(@"Collected candidate matches: {0}", GetNumberOfMatches(matches));

            progData.StatusInternal = string.Empty;
            progData.Report(100.0);
        }

        private void RunSearch(SortedSet<DatabaseSequenceSpectrumMatch>[] matches, FastaDatabase db, ISequenceFilter sequenceFilter, CancellationToken? cancellationToken = null, IProgress<ProgressData> progress = null)
        {
            var progData = new ProgressData(progress)
            {
                Status = "Searching for matches"
            };

            var sw = new Stopwatch();
            long estimatedProteins;
            var annotationsAndOffsets = GetAnnotationsAndOffsets(db, out estimatedProteins, cancellationToken);
            Console.WriteLine(@"Estimated proteins: " + estimatedProteins.ToString("#,##0"));

            var numProteins = 0;
            var lastUpdate = DateTime.MinValue; // Force original update of 0%

            sw.Reset();
            sw.Start();
            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };

            var maxNumNTermCleavages = SearchMode == InternalCleavageType.NoInternalCleavage ? MaxNumNTermCleavages : 0;
            //foreach (var annotationAndOffset in annotationsAndOffsets)
            Parallel.ForEach(annotationsAndOffsets, pfeOptions, annotationAndOffset =>
            {
                if (cancellationToken != null && cancellationToken.Value.IsCancellationRequested)
                {
                    //return matches;
                    return;
                }

                SearchProgressReport(ref numProteins, ref lastUpdate, estimatedProteins, sw, progData);
                SearchForMatches(annotationAndOffset, sequenceFilter, matches, maxNumNTermCleavages, db.IsDecoy, cancellationToken);
            });

            Console.WriteLine(@"Collected candidate matches: {0}", GetNumberOfMatches(matches));

            progData.StatusInternal = string.Empty;
            progData.Report(100.0);
        }

        private void SearchProgressReport(ref int numProteins, ref DateTime lastUpdate, long estimatedProteins, Stopwatch sw, ProgressData progData, string itemName = "proteins")
        {
            var tempNumProteins = Interlocked.Increment(ref numProteins) - 1;

            if (estimatedProteins < 1)
                estimatedProteins = 1;

            progData.StatusInternal = string.Format(@"Processing, {0} {1} done, {2:#0.0}% complete, {3:f1} sec elapsed",
                    tempNumProteins,
                    itemName,
                    tempNumProteins / (double)estimatedProteins * 100.0,
                    sw.Elapsed.TotalSeconds);
            progData.Report(tempNumProteins, estimatedProteins);

            int secondsThreshold;

            if (sw.Elapsed.TotalMinutes < 2)
                secondsThreshold = 15;      // Every 15 seconds
            else if (sw.Elapsed.TotalMinutes < 5)
                secondsThreshold = 30;      // Every 30 seconds
            else if (sw.Elapsed.TotalMinutes < 20)
                secondsThreshold = 60;      // Every 1 minute
            else
                secondsThreshold = 300;     // Every 5 minutes

            if (DateTime.UtcNow.Subtract(lastUpdate).TotalSeconds >= secondsThreshold)
            {
                lastUpdate = DateTime.UtcNow;

                Console.WriteLine(@"Processing, {0} {1} done, {2:#0.0}% complete, {3:f1} sec elapsed",
                    tempNumProteins,
                    itemName,
                    tempNumProteins / (double)estimatedProteins * 100.0,
                    sw.Elapsed.TotalSeconds);
            }
        }

        private void SearchForMatches(AnnotationAndOffset annotationAndOffset,
            ISequenceFilter sequenceFilter, SortedSet<DatabaseSequenceSpectrumMatch>[] matches, int maxNumNTermCleavages, bool isDecoy, CancellationToken? cancellationToken = null)
        {
            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };

            var annotation = annotationAndOffset.Annotation;
            var offset = annotationAndOffset.Offset;
            //var protein = db.GetProteinName(offset);
            var protSequence = annotation.Substring(2, annotation.Length - 4);
            var seqGraph = SequenceGraph.CreateGraph(AminoAcidSet, AminoAcid.ProteinNTerm, protSequence,
                AminoAcid.ProteinCTerm);

            if (seqGraph == null) return; // No matches will be found without a sequence graph.

            for (var numNTermCleavages = 0; numNTermCleavages <= maxNumNTermCleavages; numNTermCleavages++)
            {
                if (numNTermCleavages > 0) seqGraph.CleaveNTerm();
                var numProteoforms = seqGraph.GetNumProteoformCompositions();
                var modCombs = seqGraph.GetModificationCombinations();
                for (var modIndex = 0; modIndex < numProteoforms; modIndex++)
                {
                    seqGraph.SetSink(modIndex);
                    var protCompositionWithH2O = seqGraph.GetSinkSequenceCompositionWithH2O();
                    var sequenceMass = protCompositionWithH2O.Mass;

                    if (sequenceMass < MinSequenceMass || sequenceMass > MaxSequenceMass) continue;

                    var modCombinations = modCombs[modIndex];
                    var ms2ScanNums = this.ScanNumbers ?? sequenceFilter.GetMatchingMs2ScanNums(sequenceMass);

                    Parallel.ForEach(ms2ScanNums, pfeOptions, ms2ScanNum =>
                    {
                        if (ms2ScanNum > _ms2ScanNums.Last() || ms2ScanNum < _ms2ScanNums.First()) return;

                        var isoTargetMz = _isolationWindowTargetMz[ms2ScanNum];
                        if (!(isoTargetMz > 0)) return;
                        var charge = (int)Math.Round(sequenceMass / (isoTargetMz - Constants.Proton));

                        var scorer = this.fragmentScorerFactory.GetScorer(ms2ScanNum, sequenceMass, charge);
                        var score = seqGraph.GetFragmentScore(scorer);

                        var precursorIon = new Ion(protCompositionWithH2O, charge);
                        var sequence = protSequence.Substring(numNTermCleavages);
                        var pre = numNTermCleavages == 0 ? annotation[0] : annotation[numNTermCleavages + 1];
                        var post = annotation[annotation.Length - 1];
                        var prsm = new DatabaseSequenceSpectrumMatch(sequence, pre, post, ms2ScanNum, offset, numNTermCleavages,
                            modCombinations, precursorIon, score, isDecoy);

                        AddMatch(matches, ms2ScanNum, prsm);
                    });
                }
            }
        }

        private void AddMatch(SortedSet<DatabaseSequenceSpectrumMatch>[] matches, int ms2ScanNum, DatabaseSequenceSpectrumMatch prsm)
        {
            lock (matches)
            {
                if (matches[ms2ScanNum] == null)
                {
                    matches[ms2ScanNum] = new SortedSet<DatabaseSequenceSpectrumMatch> {prsm};
                }
                else // already exists
                {
                    var existingMatches = matches[ms2ScanNum];
                    //var maxScore = existingMatches.Max.Score;
                    if (existingMatches.Count < NumMatchesPerSpectrum)
                    {
                        //if (!(maxScore*0.7 < prsm.Score)) return;
                        existingMatches.Add(prsm);
                    }
                    else
                    {
                        var minScore = existingMatches.Min.Score;
                        if (!(prsm.Score > minScore)) return;
                        existingMatches.Add(prsm);
                        existingMatches.Remove(existingMatches.Min);
                    }
                    //if (NumMatchesPerSpectrum > 1) existingMatches.RemoveWhere(mt => mt.Score < maxScore * 0.7);
                }
            }
        }

        private SequenceTagGenerator GetSequenceTagGenerator(CancellationToken? cancellationToken = null, IProgress<ProgressData> progress = null)
        {
            var sequenceTagGen = new SequenceTagGenerator(_run, new Tolerance(5));
            var scanNums = _ms2ScanNums;

            var progData = new ProgressData(progress)
            {
                Status = "Generating sequence tags"
            };

            var sw = new Stopwatch();

            // Rescore and Estimate #proteins for GF calculation
            long estimatedProteins = scanNums.Count;
            Console.WriteLine(@"Number of spectra: " + estimatedProteins);
            var numProteins = 0;
            var lastUpdate = DateTime.MinValue; // Force original update of 0%
            sw.Reset();
            sw.Start();

            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };

            Parallel.ForEach(scanNums, pfeOptions, scanNum =>
            {
                sequenceTagGen.Generate(scanNum);
                SearchProgressReport(ref numProteins, ref lastUpdate, estimatedProteins, sw, progData,
                                     "spectra");
            });

            progData.StatusInternal = string.Empty;
            progData.Report(100.0);
            Console.WriteLine(@"Generated sequence tags: " + sequenceTagGen.NumberOfGeneratedTags());
            return sequenceTagGen;
        }

        private LinkedList<Tuple<double, ScoreDistribution>>[] _cachedScoreDistributions;
        private DatabaseSequenceSpectrumMatch[] RunGeneratingFunction(SortedSet<DatabaseSequenceSpectrumMatch>[] sortedMatches, CancellationToken? cancellationToken = null, IProgress<ProgressData> progress = null)
        {
            var progData = new ProgressData(progress)
            {
                Status = "Calculating spectral E-values for matches"
            };

            if (_cachedScoreDistributions == null)
            {
                _cachedScoreDistributions = new LinkedList<Tuple<double, ScoreDistribution>>[_run.MaxLcScan + 1];
                foreach (var scanNum in _ms2ScanNums)
                    _cachedScoreDistributions[scanNum] = new LinkedList<Tuple<double, ScoreDistribution>>();
            }

            var sw = new Stopwatch();

            var topDownScorer = new InformedTopDownScorer(_run, AminoAcidSet, MinProductIonCharge, MaxProductIonCharge, ProductIonTolerance);

            // Rescore and Estimate #proteins for GF calculation
            var matches = new LinkedList<DatabaseSequenceSpectrumMatch>[sortedMatches.Length];
            long estimatedProteins = 0;
            foreach(var scanNum in _ms2ScanNums)
            {
                var prsms = sortedMatches[scanNum];
                if (prsms == null) continue;
                var spec = _run.GetSpectrum(scanNum) as ProductSpectrum;
                if (spec == null || spec.Peaks.Length == 0)
                    continue;

                foreach (var match in prsms)
                {
                    var sequence = match.Sequence;
                    var ion = match.Ion;

                    // Re-scoring
                    var scores = topDownScorer.GetScores(spec, sequence, ion.Composition, ion.Charge, scanNum);
                    if (scores == null) continue;

                    match.Score = scores.Score;
                    match.ModificationText = scores.Modifications;
                    match.NumMatchedFragments = scores.NumMatchedFrags;
                    if (match.Score > CompositeScorer.ScoreParam.Cutoff)
                    {
                        if (matches[scanNum] == null) matches[scanNum] = new LinkedList<DatabaseSequenceSpectrumMatch>();
                        matches[scanNum].AddLast(match);
                    }
                }

                if (matches[scanNum] != null) estimatedProteins += matches[scanNum].Count;
            }

            Console.WriteLine(@"Estimated matched proteins: " + estimatedProteins.ToString("#,##0"));

            var numProteins = 0;
            var lastUpdate = DateTime.MinValue; // Force original update of 0%
            sw.Reset();
            sw.Start();

            var scanNums = _ms2ScanNums.Where(scanNum => matches[scanNum] != null).ToArray();

            var pfeOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumThreads,
                CancellationToken = cancellationToken ?? CancellationToken.None
            };

            Parallel.ForEach(scanNums, pfeOptions, scanNum =>
            {
                var currentTask = "?";
                try
                {
                    var scoreDistributions = _cachedScoreDistributions[scanNum];
                    foreach (var match in matches[scanNum])
                    {
                        var currentIteration = "for scan " + scanNum + " and mass " + match.Ion.Composition.Mass;
                        currentTask = "Calling GetMs2ScoringGraph " + currentIteration;

                        var graph = _ms2ScorerFactory2.GetMs2ScoringGraph(scanNum, match.Ion.Composition.Mass);
                        if (graph == null) continue;

                        currentTask = "Calling ComputeGeneratingFunction " + currentIteration;

                        var scoreDist = (from distribution in scoreDistributions
                                         where Math.Abs(distribution.Item1 - match.Ion.Composition.Mass) < PrecursorIonTolerance.GetToleranceAsTh(match.Ion.Composition.Mass)
                                         select distribution.Item2).FirstOrDefault();
                        if (scoreDist == null)
                        {
                            var gf = new GeneratingFunction(graph);
                            gf.ComputeGeneratingFunction();
                            scoreDist = gf.GetScoreDistribution();
                            scoreDistributions.AddLast(new Tuple<double, ScoreDistribution>(match.Ion.Composition.Mass, scoreDist));
                        }

                        currentTask = "Calling GetSpectralEValue " + currentIteration + " and score " + (int)match.Score;
                        match.SpecEvalue = scoreDist.GetSpectralEValue(match.Score);

                        currentTask = "Reporting progress " + currentIteration;
                        SearchProgressReport(ref numProteins, ref lastUpdate, estimatedProteins, sw, progData);
                    }
                }
                catch (Exception ex)
                {
                    var errMsg = string.Format("Exception while {0}: {1}", currentTask, ex.Message);
                    Console.WriteLine(errMsg);
                    throw new Exception(errMsg, ex);
                }
            });

            var finalMatches = new DatabaseSequenceSpectrumMatch[matches.Length];
            foreach (var scanNum in scanNums)
            {
                finalMatches[scanNum] = matches[scanNum].OrderBy(m => m.SpecEvalue).First();
            }

            progData.StatusInternal = string.Empty;
            progData.Report(100.0);
            return finalMatches;
        }

        private int GetNumberOfMatches(SortedSet<DatabaseSequenceSpectrumMatch>[] matches)
        {
            var nMatches = 0;
            lock (matches)
            {
                nMatches += _ms2ScanNums.Where(scanNum => matches[scanNum] != null).Sum(scanNum => matches[scanNum].Count);
            }
            return nMatches;
        }

        private List<DatabaseSearchResultData> WriteResultsToFile(DatabaseSequenceSpectrumMatch[] matches, string outputFilePath, FastaDatabase database)
        {
            var results = CreateResults(matches, database).ToList();
            DatabaseSearchResultData.WriteResultsToFile(outputFilePath, results, false);
            return results;
        }

        private IEnumerable<DatabaseSearchResultData> CreateResults(DatabaseSequenceSpectrumMatch[] matches, FastaDatabase database)
        {
            foreach (var scanNum in _ms2ScanNums)
            {
                var match = matches[scanNum];
                if (match == null)
                    continue;

                var start = database.GetOneBasedPositionInProtein(match.Offset) + 1 + match.NumNTermCleavages;
                var proteinName = database.GetProteinName(match.Offset);
                var result = new DatabaseSearchResultData()
                {
                    ScanNum = scanNum,
                    Pre = match.Pre.ToString(),
                    Sequence = match.Sequence,
                    Post = match.Post.ToString(),
                    Modifications = match.ModificationText,
                    Composition = match.Ion.Composition.ToString(),
                    ProteinName = proteinName,
                    ProteinLength = database.GetProteinLength(proteinName),
                    ProteinDescription = database.GetProteinDescription(match.Offset),
                    Start = start,
                    End = start + match.Sequence.Length - 1,
                    Charge = match.Ion.Charge,
                    MostAbundantIsotopeMz = match.Ion.GetMostAbundantIsotopeMz(),
                    Mass = match.Ion.Composition.Mass,
                    NumMatchedFragments = match.NumMatchedFragments,
                    Probability = CompositeScorer.GetProbability(match.Score),
                    SpecEValue = match.SpecEvalue,
                    EValue = match.SpecEvalue * database.GetNumEntries()
                };

                yield return result;
            }
        }

        private void WriteResultsToMzid(IEnumerable<DatabaseSearchResultData> matches, string outputFilePath, FastaDatabase database, LcMsRun lcmsRun)
        {
            var datasetName = Path.GetFileNameWithoutExtension(outputFilePath);
            var creator = new IdentDataCreator("MSPathFinder_" + datasetName, "MSPathFinder_" + datasetName);
            var soft = creator.AddAnalysisSoftware("Software_1", "MSPathFinder", "1.3", CV.CVID.CVID_Unknown, "MSPathFinder");
            var settings = creator.AddAnalysisSettings(soft, "Settings_1", CV.CVID.MS_ms_ms_search);
            var searchDb = creator.AddSearchDatabase(database.GetFastaFilePath(), database.GetNumEntries(), Path.GetFileNameWithoutExtension(database.GetFastaFilePath()), CV.CVID.CVID_Unknown,
                CV.CVID.MS_FASTA_format);

            if (RunTargetDecoyAnalysis == DatabaseSearchMode.Both)
            {
                searchDb.CVParams.AddRange(new CVParamObj[]
                {
                    new CVParamObj() { Cvid = CV.CVID.MS_DB_composition_target_decoy, },
                    new CVParamObj() { Cvid = CV.CVID.MS_decoy_DB_accession_regexp, Value = "^XXX", },
                    //new CVParamObj() { Cvid = CV.CVID.MS_decoy_DB_type_reverse, },
                    new CVParamObj() { Cvid = CV.CVID.MS_decoy_DB_type_randomized, },
                });
            }

            // store the settings...
            CreateMzidSettings(settings);

            var path = SpecFilePath;
            // TODO: fix this to match correctly to the original file - May need to modify the PBF format to add an input format specifier
            var specData = creator.AddSpectraData(path, datasetName, CV.CVID.MS_Thermo_nativeID_format,
                CV.CVID.MS_Thermo_RAW_format);

            foreach (var match in matches)
            {
                var scanNum = match.ScanNum;
                var spec = lcmsRun.GetSpectrum(scanNum, false);
                var matchIon = new Ion(Composition.Parse(match.Composition), match.Charge);

                var specIdent = creator.AddSpectrumIdentification(specData, spec.NativeId, spec.ElutionTime, match.MostAbundantIsotopeMz,
                    match.Charge, 1, double.NaN);
                specIdent.CalculatedMassToCharge = matchIon.GetMonoIsotopicMz();
                var pep = new PeptideObj(match.Sequence);

                // Get the search modifications as they were passed into the AminoAcidSet constructor, so we can retrieve masses from them
                var modDict = new Dictionary<string, Modification>();
                foreach (var mod in AminoAcidSet.SearchModifications)
                {
                    modDict.Add(mod.Modification.Name, mod.Modification);
                }
                var modText = match.Modifications;
                if (!string.IsNullOrWhiteSpace(modText))
                {
                    var mods = modText.Split(',');
                    foreach (var mod in mods)
                    {
                        var tokens = mod.Split(' ');
                        var modInfo = modDict[tokens[0]];
                        var modObj = new ModificationObj(CV.CVID.MS_unknown_modification, tokens[0], int.Parse(tokens[1]), modInfo.Mass);
                        pep.Modifications.Add(modObj);
                    }
                }
                specIdent.Peptide = pep;

                var proteinName = match.ProteinName;
                var protLength = match.ProteinLength;
                var proteinDescription = match.ProteinDescription;
                var dbSeq = new DbSequenceObj(searchDb, protLength, proteinName, proteinDescription);

                var start = match.Start;
                var end = match.End;
                var pepEv = new PeptideEvidenceObj(dbSeq, pep, start, end, match.Pre, match.Post, false);
                specIdent.AddPeptideEvidence(pepEv);

                var probability = match.Probability;

                specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_chemical_compound_formula, Value = match.Composition, });
                specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_number_of_matched_peaks, Value = match.NumMatchedFragments.ToString(), });
                specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_SEQUEST_probability, Value = probability.ToString(CultureInfo.InvariantCulture), });
                specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_MS_GF_SpecEValue, Value = match.SpecEValue.ToString(CultureInfo.InvariantCulture), });
                specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_MS_GF_EValue, Value = match.EValue.ToString(CultureInfo.InvariantCulture), });
                if (match.HasTdaScores)
                {
                    specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_MS_GF_QValue, Value = match.QValue.ToString(CultureInfo.InvariantCulture), });
                    specIdent.CVParams.Add(new CVParamObj() { Cvid = CV.CVID.MS_MS_GF_PepQValue, Value = match.PepQValue.ToString(CultureInfo.InvariantCulture), });
                }
            }

            var identData = creator.GetIdentData();

            MzIdentMlReaderWriter.Write(new MzIdentMLType(identData), outputFilePath);
        }

        private void CreateMzidSettings(SpectrumIdentificationProtocolObj settings)
        {
            settings.AdditionalSearchParams.Items.AddRange(new ParamBaseObj[]
            {
                new CVParamObj(CV.CVID.MS_parent_mass_type_mono),
                new CVParamObj(CV.CVID.MS_fragment_mass_type_mono),
                new UserParamObj() { Name = "TargetDecoyApproach", Value = (RunTargetDecoyAnalysis == DatabaseSearchMode.Both).ToString()},
                new UserParamObj() { Name = "MinSequenceLength", Value = MinSequenceLength.ToString() },
                new UserParamObj() { Name = "MaxSequenceLength", Value = MaxSequenceLength.ToString() },
                new UserParamObj() { Name = "MaxNumNTermCleavages", Value = MaxNumNTermCleavages.ToString() },
                new UserParamObj() { Name = "MaxNumCTermCleavages", Value = MaxNumCTermCleavages.ToString() },
                new UserParamObj() { Name = "MinPrecursorIonCharge", Value = MinPrecursorIonCharge.ToString() },
                new UserParamObj() { Name = "MaxPrecursorIonCharge", Value = MaxPrecursorIonCharge.ToString() },
                new UserParamObj() { Name = "MinProductIonCharge", Value = MinProductIonCharge.ToString() },
                new UserParamObj() { Name = "MaxProductIonCharge", Value = MaxProductIonCharge.ToString() },
                new UserParamObj() { Name = "MinSequenceMass", Value = MinSequenceMass.ToString(CultureInfo.InvariantCulture) },
                new UserParamObj() { Name = "MaxSequenceMass", Value = MaxSequenceMass.ToString(CultureInfo.InvariantCulture) },
                new UserParamObj() { Name = "PrecursorIonTolerance", Value = PrecursorIonTolerance.ToString() },
                new UserParamObj() { Name = "ProductIonTolerance", Value = ProductIonTolerance.ToString() },
                new UserParamObj() { Name = "SearchMode", Value = SearchMode.ToString() },
                new UserParamObj() { Name = "NumMatchesPerSpectrum", Value = NumMatchesPerSpectrum.ToString() },
                new UserParamObj() { Name = "TagBasedSearch", Value = TagBasedSearch.ToString() },
            });

            // Get the search modifications as they were passed into the AminoAcidSet constructor...
            foreach (var mod in AminoAcidSet.SearchModifications)
            {
                var modObj = new SearchModificationObj()
                {
                    FixedMod = mod.IsFixedModification,
                    MassDelta = (float)mod.Modification.Mass,
                    Residues = mod.TargetResidue.ToString(),
                };
                // "*" is used for wildcard residue N-Term or C-Term modifications. mzIdentML standard says that "." should be used instead.
                if (modObj.Residues.Contains("*"))
                {
                    modObj.Residues = modObj.Residues.Replace("*", ".");
                }
                // Really only using this for the modification name parsing for CVParams that exists with ModificationObj
                var tempMod = new ModificationObj(CV.CVID.MS_unknown_modification, mod.Modification.Name, 0, modObj.MassDelta);
                modObj.CVParams.Add(tempMod.CVParams.First());
                settings.ModificationParams.Add(modObj);
            }

            // No enzyme for top-down search
            //settings.Enzymes.Enzymes.Add(new EnzymeObj());

            settings.ParentTolerances.AddRange(new CVParamObj[]
            {
                new CVParamObj(CV.CVID.MS_search_tolerance_plus_value, PrecursorIonTolerancePpm.ToString(CultureInfo.InvariantCulture)) { UnitCvid = CV.CVID.UO_parts_per_million },
                new CVParamObj(CV.CVID.MS_search_tolerance_minus_value, PrecursorIonTolerancePpm.ToString(CultureInfo.InvariantCulture)) { UnitCvid = CV.CVID.UO_parts_per_million },
            });
            settings.FragmentTolerances.AddRange(new CVParamObj[]
            {
                new CVParamObj(CV.CVID.MS_search_tolerance_plus_value, ProductIonTolerancePpm.ToString(CultureInfo.InvariantCulture)) { UnitCvid = CV.CVID.UO_parts_per_million },
                new CVParamObj(CV.CVID.MS_search_tolerance_minus_value, ProductIonTolerancePpm.ToString(CultureInfo.InvariantCulture)) { UnitCvid = CV.CVID.UO_parts_per_million },
            });
            settings.Threshold.Items.Add(new CVParamObj(CV.CVID.MS_no_threshold));
        }
    }
}
