import unittest
from history import clean_history


def price(day,close=100,adj=100):
    return dict(date=day,Open=close,High=close,Low=close,Close=close,**{'Adj Close':adj,'Volume':100})


class HistoryTests(unittest.TestCase):
    def test_predecessor_is_not_current_company(self):
        good,excluded,_=clean_history('3711',[price('2018-04-27'),price('2018-04-30')],'2018-04-30')
        self.assertEqual([r['date'] for r in good],['2018-04-30'])
        self.assertEqual(excluded[0]['reason'],'before_current_entity_listing')

    def test_missing_adjustment_never_falls_back(self):
        good,excluded,_=clean_history('2330',[price('2020-01-02',adj=''),price('2020-01-03')])
        self.assertEqual(len(good),1)
        self.assertEqual(excluded[0]['date'],'2020-01-02')

    def test_price_index_uses_close_not_adjusted(self):
        good,_,_=clean_history('^TWII',[price('2020-01-02',close=100,adj=110)])
        self.assertEqual(good[0]['adjusted_close'],100)

    def test_jumps_are_flagged_not_silently_repaired(self):
        good,_,flags=clean_history('0050',[price('2013-12-31',adj=100),price('2014-01-02',adj=25)])
        self.assertEqual(good[-1]['adjusted_close'],25)
        self.assertEqual(flags[0]['simpleReturn'],-.75)

    def test_duplicate_day_rejected(self):
        with self.assertRaisesRegex(ValueError,'Duplicate'):
            clean_history('2330',[price('2020-01-02'),price('2020-01-02')])
