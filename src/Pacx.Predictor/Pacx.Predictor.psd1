@{
    RootModule           = 'Pacx.Predictor.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = '9b3f2c44-7a6e-4d1b-8c0f-5e2a1d7b9c31'
    Author               = 'Keno Schürger'
    Copyright            = '(c) 2026 Keno Schürger. MIT License.'
    Description          = 'Inline command predictions for PACX (Greg.Xrm.Command) in PowerShell 7.4+. Community tool, not part of PACX.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @()
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @('pacx', 'Dataverse', 'PowerPlatform', 'PSReadLine', 'Predictor', 'Completion')
            LicenseUri = 'https://github.com/Keno-fsdf/pacx-predictor/blob/main/LICENSE'
            ProjectUri = 'https://github.com/Keno-fsdf/pacx-predictor'
        }
    }
}
