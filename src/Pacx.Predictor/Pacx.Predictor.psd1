@{
    RootModule           = 'Pacx.Predictor.dll'
    ModuleVersion        = '0.3.1'
    GUID                 = '9b3f2c44-7a6e-4d1b-8c0f-5e2a1d7b9c31'
    Author               = 'Keno Schürger'
    Copyright            = '(c) 2026 Keno Schürger. MIT License.'
    Description          = 'Inline command predictions for PACX (Greg.Xrm.Command) and for the Power Platform CLI (pac) in PowerShell 7.4+. Community tool, not part of either.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @()
    CmdletsToExport      = @('Update-PacPredictor')
    VariablesToExport    = @()
    AliasesToExport      = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @('pacx', 'pac', 'Dataverse', 'PowerPlatform', 'PSReadLine', 'Predictor', 'Completion')
            LicenseUri = 'https://github.com/Keno-fsdf/pacx-predictor/blob/main/LICENSE'
            ProjectUri = 'https://github.com/Keno-fsdf/pacx-predictor'
        }
    }
}
