import base64
import hashlib
import hmac
from unittest import TestCase
from app.security import AuthenticationError, decode_access_token
from app.settings import settings

class TokenTests(TestCase):
    def test_malformed_signature_is_authentication_error(self):
        for token in ('x.y.a', 'x.y.é'):
            with self.assertRaises(AuthenticationError):
                decode_access_token(token)

    def test_signed_non_object_payload_is_authentication_error(self):
        def encode(value):
            return base64.urlsafe_b64encode(value).rstrip(b'=')
        for header, payload in [(b'[]', b'{}'), (b'{"alg":"HS256","typ":"JWT"}', b'[]'),
                                (b'{"alg":"HS256","typ":"JWT"}', b'\xff')]:
            message = encode(header) + b'.' + encode(payload)
            signature = hmac.new(settings.jwt_secret_key.encode(), message, hashlib.sha256).digest()
            with self.assertRaises(AuthenticationError):
                decode_access_token((message + b'.' + encode(signature)).decode())
