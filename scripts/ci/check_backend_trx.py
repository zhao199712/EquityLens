"""Fail closed if backend tests fail, are skipped, or omit PostgreSQL suites."""
import argparse
import xml.etree.ElementTree as ET


def validate(path, required_classes=()):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = root.findall(".//t:UnitTestResult", ns)
    if not results or any(r.get("outcome") != "Passed" for r in results):
        raise ValueError("Backend results must be nonempty, all passed, with zero skipped tests")
    definitions = {
        test.get("id"): method.get("className", "")
        for test in root.findall(".//t:UnitTest", ns)
        if (method := test.find("t:TestMethod", ns)) is not None
    }
    executed_ids = {r.get("testId") for r in results}
    if required_classes:
        for name in required_classes:
            ids = {key for key, value in definitions.items() if value.split(",")[0].endswith("." + name)}
            if not ids or not ids.issubset(executed_ids):
                raise ValueError(f"Missing executed suite: {name}")
        print(f"Cross-service: {len(results)} passed; required suites present; zero skipped")
        return
    # Test definitions give fully qualified class names independently of display names.
    integration_ids = {
        test.get("id") for test in root.findall(".//t:UnitTest", ns)
        if (method := test.find("t:TestMethod", ns)) is not None
        and ".Integration." in method.get("className", "")
        and "Postgres" in method.get("className", "")
    }
    executed_ids = {r.get("testId") for r in results}
    if not integration_ids or not integration_ids.issubset(executed_ids):
        raise ValueError("Missing executed PostgreSQL integration tests")
    print(f"Backend: {len(results)} passed; {len(integration_ids)} PostgreSQL integration tests; zero skipped")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("path")
    parser.add_argument("--required-class", action="append", default=[])
    args = parser.parse_args()
    validate(args.path, args.required_class)
