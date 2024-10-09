# Generating Test Certificates

* Use WSL to access OpenSSL (the keys in this folder was generated using Ubuntu)
* All private key files uses the password "password"

## 1. Generate Root CA

```
openssl req -x509 -new -nodes -key root_private_key.pem -sha256 -days 3650 -out root_cert.pem -config root_cert_config.cnf
``` 

* This will generate the **"root_cert.pem"** that is valid for 10 years.
* Open the **"root_private_key.pem"** and copy-paste its content to the end of the **"root_cert.pem"** file.

## 2. Generate CSR

```
openssl req -new -key server_private_key.pem -out server.csr -config server_cert_config.cnf
```

* This generates the **"server.csr"** file.

## 3. Generate The Server Certificate

```
openssl x509 -req -in server.csr -CA root_cert.pem -CAkey root_private_key.pem -CAcreateserial -out server_cert.pem -days 365 -sha256 -extfile v3_ext.cnf
```

* This generates the **"server_cert.pem"** file.
* Open the **"server_private_key.pem"** and copy-paste its content to the end of the **"server_cert.pem"** file.