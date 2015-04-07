# EOBO Signer args: 89A22802E373A986C9961D414422A873B912B05E CSIS\Mike request.csr cert.crt
# TODO: Verify path to yubico-piv-tool

$enrollmentAgent = "89A22802E373A986C9961D414422A873B912B05E"
$mgmKey = "010203040506070801020304050607080102030405060708"
$user = Read-Host 'Input User (Domain\User)'

$pin = Read-Host 'Input PIN' -AsSecureString
$pin = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pin))

# Set CHUID
Write-Host "Setting CHUID"

Start-Process .\bin\yubico-piv-tool -ArgumentList "-k $mgmKey -a set-chuid" -Wait -NoNewWindow

# Generate key
Write-Host "Generating key on Yubikey"
Start-Process .\bin\yubico-piv-tool -ArgumentList "-k $mgmKey -s 9a -a generate -o public.pem" -Wait -NoNewWindow

# Make CSR
Write-Host "Generating CSR"

iex ".\bin\yubico-piv-tool -a verify-pin -P $pin -s 9a -a request-certificate -S '/CN=test/OU=example/' -i public.pem -o request.csr"
# Start-Process .\bin\yubico-piv-tool -ArgumentList '-a verify-pin -P $pin -s 9a -a request-certificate -S "/CN=test/OU=example/" -i public.pem -o request.csr' -Wait -NoNewWindow

# Sign CSR
Write-Host "Signing CSR for user $user"

Start-Process .\EOBOSigner.exe -ArgumentList "$enrollmentAgent $user request.csr cert.crt" -Wait # -NoNewWindow

# Import Cert
Write-Host "Importing certificate"

Start-Process .\bin\yubico-piv-tool -ArgumentList "-k $mgmKey -s 9a -a import-certificate -i cert.crt" -Wait -NoNewWindow

# Cleanup
Write-Host "Cleaning up"

# Remove-Item cert.crt
# Remove-Item request.csr
# Remove-Item public.pem