﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InformedProteomics.Backend.Data.Biology;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.Database;
using InformedProteomics.Backend.MassSpecData;
using InformedProteomics.TopDown.Scoring;

namespace InformedProteomics.TopDown.Execution
{
    public class IcTopDown
    {
        // Consider all subsequenes of lengths [minProteinLength,maxProteinLength]
        public IcTopDown(
            string dbFilePath,
            string specFilePath,
            AminoAcidSet aaSet,
            int minProteinLength = 30,
            int maxProteinLength = 250,
            int minPrecursorIonCharge = 3,
            int maxPrecursorIonCharge = 30,
            int minProductIonCharge = 1,
            int maxProductIonCharge = 10,
            double precursorIonTolerancePpm = 10,
            double productIonTolerancePpm = 15,
            bool runTargetDecoyAnalysis = true)
        {

        }

        // Consider intact sequences with N- and C-terminal cleavages
        public IcTopDown(
            string dbFilePath, 
            string specFilePath, 
            AminoAcidSet aaSet,
            int minProteinLength = 30, 
            int maxProteinLength = 250, 
            int maxNumNTermCleavages = 30,
            int maxNumCTermCleavages = 0,
            int minPrecursorIonCharge = 3, 
            int maxPrecursorIonCharge = 30,
            int minProductIonCharge = 1, 
            int maxProductIonCharge = 10,
            double precursorIonTolerancePpm = 10,
            double productIonTolerancePpm = 15,
            bool runTargetDecoyAnalysis = true)
        {
            
        }

        public void TestTopDownSearch(
            string dbFilePath, string specFilePath, AminoAcidSet aaSet,
            int minLength, int maxLength, int maxNumNTermCleavages,
            int minPrecursorIonCharge, int maxPrecursorIonCharge,
            int minProductIonCharge, int maxProductIonCharge,
            Tolerance precursorTolerance, Tolerance productIonTolerance,
            bool ultraMod,
            bool isDecoy
            )
        {
        }
    }
}
