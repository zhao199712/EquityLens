import tempfile
import unittest
from pathlib import Path
from check_backend_trx import validate


def report(outcome='Passed', integration=True, include_result=True):
    class_name = 'EquityLens.Api.Tests.Integration.AgentLeasePostgresTests' if integration else 'UnitTests'
    result = f'<UnitTestResult testId="1" outcome="{outcome}" />' if include_result else ''
    return f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <TestDefinitions><UnitTest id="1"><TestMethod className="{class_name}" /></UnitTest></TestDefinitions>
      <Results>{result}</Results></TestRun>'''


class TrxGuardTests(unittest.TestCase):
    def check(self, xml):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'tests.trx'
            path.write_text(xml)
            validate(path)

    def test_passed_postgres_is_accepted(self):
        self.check(report())

    def test_skipped_or_failed_test_is_rejected(self):
        for outcome in ['NotExecuted', 'Failed']:
            with self.subTest(outcome=outcome), self.assertRaises(ValueError):
                self.check(report(outcome))

    def test_unit_only_report_is_rejected(self):
        with self.assertRaises(ValueError):
            self.check(report(integration=False))

    def test_empty_results_are_rejected(self):
        with self.assertRaises(ValueError):
            self.check(report(include_result=False))

    def test_missing_result_for_discovered_integration_test_is_rejected(self):
        xml = report().replace('</TestDefinitions>', '<UnitTest id="2"><TestMethod className="EquityLens.Api.Tests.Integration.ToolApprovalPostgresTests" /></UnitTest></TestDefinitions>')
        with self.assertRaises(ValueError):
            self.check(xml)


if __name__ == '__main__':
    unittest.main()
