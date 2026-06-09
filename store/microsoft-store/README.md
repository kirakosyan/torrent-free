# Microsoft Store listings

Use `listings.csv` with Partner Center's **Import listings** action after uploading the MSIX packages.

To upload the included Store logo PNGs, choose **Import folder** and select the `store/microsoft-store` folder. The CSV paths include the root folder name (`microsoft-store/assets/...`) as Partner Center expects for folder imports.

The MSIX package manifest controls the languages shown under **Languages supported in packages**. The CSV controls the customer-facing Store listing text for each language. Partner Center keeps these as separate submission metadata.

If Partner Center's exported CSV already contains screenshot URLs, keep those asset rows from the export and copy these language columns into that latest exported template before importing. Microsoft requires a description and at least one screenshot for every completed listing.
