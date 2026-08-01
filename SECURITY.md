# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in ApexAstro, please report it responsibly by opening a **private security advisory** on GitHub rather than using public issues.

### How to Report

1. Go to the [ApexAstro GitHub repository](https://github.com/kfaubel/ApexAstro)
2. Click on **Security** tab → **Advisories** → **Report a vulnerability**
3. Provide:
   - A description of the vulnerability
   - Steps to reproduce (if applicable)
   - Potential impact
   - Any suggested fixes (optional)

Alternatively, if you prefer, you can email security details to the maintainer privately.

### What to Expect

- We will acknowledge receipt of your report within 7 days
- We will investigate and work toward a fix
- We will keep you informed of our progress
- Once patched, we will release a security update and credit you (if desired)

### Security Considerations

- ApexAstro reads and displays local image files (FITS, XISF, JPG, PNG, etc.)
- It does not transmit user data over the network
- It does not require internet connectivity to function
- Session metadata is read from local CSV files (AcquisitionDetails.csv, ImageMetaData.csv)

### Supported Versions

Security updates are provided for:
- **Current release** - Latest stable version
- **Previous minor version** - If critical

Please keep ApexAstro updated to the latest version to receive security patches.

## Dependencies

ApexAstro uses the following key dependencies:
- **.NET 8 Desktop Runtime** - Provided by Microsoft
- **Community Toolkit MVVM** - Microsoft's open-source library
- **Standard WPF libraries** - Built into Windows

We regularly monitor dependencies for security updates and provide patches as needed.
