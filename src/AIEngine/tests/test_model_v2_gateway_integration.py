"""
test_model_v2_gateway_integration.py
Proxy wrapper nạp từ scripts/data_collection/tests/test_model_v2_gateway_integration.py
phục vụ chạy đồng bộ trong unittest discover của AIEngine test suite.
"""

import os
import sys

CUR_DIR = os.path.dirname(os.path.abspath(__file__))
WORKSPACE_ROOT = os.path.abspath(os.path.join(CUR_DIR, "../../../.."))
SCRIPTS_TEST_DIR = os.path.join(WORKSPACE_ROOT, "TicketShield", "scripts", "data_collection", "tests")

for p in [SCRIPTS_TEST_DIR, WORKSPACE_ROOT]:
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    import test_model_v2_gateway_integration as int_mod  # type: ignore
    TestModelV2GatewayIntegration = int_mod.TestModelV2GatewayIntegration
except (ImportError, AttributeError):
    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "gateway_integration_mod",
        os.path.join(SCRIPTS_TEST_DIR, "test_model_v2_gateway_integration.py")
    )
    if spec and spec.loader:
        gw_mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(gw_mod)
        TestModelV2GatewayIntegration = gw_mod.TestModelV2GatewayIntegration  # type: ignore

if __name__ == "__main__":
    import unittest
    unittest.main()
