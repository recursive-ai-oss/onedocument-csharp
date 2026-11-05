#!/usr/bin/env python3
"""Writes the statistical and date/time function grids: testdata/calc/function-grid/{statistical,datetime}.fgrid and the matching
.expected.tsv files with an empty answer column. For every function the argument domains below (typical values, boundaries, coercions, errors and
array shapes) are crossed and thinned to at most CAP cases by a fixed stride, so regenerating gives the same files.

usage: gen.py OUTDIR
"""
import itertools
import os
import sys

CAP = 150

NUM = ['0', '1', '-1', '2.5', '0.5', '100', '1E+10', '"3"', 'TRUE', '""', '"x"', '#N/A']
ARR = ['{1,2,3,4,5}', '{5,3,1,4,2}', '{1,2,2,3,3,3}', '{1,"a",TRUE,3}', '{1;2;3;4}', '{1,2;3,4}', '{"x"}', '{#N/A,1}', '{0,0,0}', '{2,4,4,4,5,5,7,9}']
ARR2 = ['{2,4,6,8,10}', '{1,3,2,5,4}', '{1,1,1,1,1}', '{1,2,3}', '{1,"a",3,4,5}', '{3;6;9;12;15}']
P = ['0', '0.001', '0.1', '0.5', '0.9', '0.999', '1', '-0.1', '1.1', '"0.5"']
DF = ['1', '2', '5', '10.7', '0', '-1', '1000', '"2"', '1E+11']
X = ['0', '0.5', '1', '2', '-1', '10', '100', '"1"', '"a"']
SD = ['1', '0.5', '0', '-1', '10']
BOOL = ['TRUE', 'FALSE', '1', '0', '"TRUE"']
COUNT = ['0', '1', '5', '10', '60', '100', '2.9', '-1']
PROB = ['0', '0.25', '0.5', '1', '1.5']
CRIT = ['">2"', '"<=3"', '"=a"', '"<>"', '"*"', '2', '"?"']

STAT = {
    'COUNT': [ARR + NUM, ARR2], 'COUNTA': [ARR + NUM], 'COUNTBLANK': [ARR, ['0']],
    'COUNTIF': [ARR, CRIT], 'COUNTIFS': [ARR, CRIT, ARR2, CRIT],
    'AVERAGE': [ARR + NUM, NUM], 'AVERAGEA': [ARR + NUM, NUM], 'AVERAGEIF': [ARR, CRIT, ARR2], 'AVERAGEIFS': [ARR, ARR, CRIT],
    'MAX': [ARR + NUM, NUM], 'MAXA': [ARR + NUM, NUM], 'MIN': [ARR + NUM, NUM], 'MINA': [ARR + NUM, NUM],
    'MAXIFS': [ARR, ARR, CRIT], 'MINIFS': [ARR, ARR, CRIT],
    'MEDIAN': [ARR + NUM, NUM], 'MODE': [ARR], 'MODE.SNGL': [ARR], 'MODE.MULT': [ARR],
    'LARGE': [ARR, NUM + ['3', '6']], 'SMALL': [ARR, NUM + ['3', '6']],
    'RANK': [NUM, ARR, ['0', '1', 'TRUE']], 'RANK.EQ': [NUM, ARR, ['0', '1']], 'RANK.AVG': [NUM, ARR, ['0', '1']],
    'PERCENTILE': [ARR, P], 'PERCENTILE.INC': [ARR, P], 'PERCENTILE.EXC': [ARR, P],
    'QUARTILE': [ARR, ['0', '1', '2', '3', '4', '5', '-1', '1.7']], 'QUARTILE.INC': [ARR, ['0', '1', '2', '3', '4', '5']], 'QUARTILE.EXC': [ARR, ['0', '1', '2', '3', '4']],
    'PERCENTRANK': [ARR, NUM, ['1', '2', '3', '0']], 'PERCENTRANK.INC': [ARR, NUM, ['1', '3', '0']], 'PERCENTRANK.EXC': [ARR, NUM, ['1', '3', '0']],
    'VAR': [ARR + NUM], 'VAR.S': [ARR + NUM], 'VAR.P': [ARR + NUM], 'VARP': [ARR + NUM], 'VARA': [ARR + NUM], 'VARPA': [ARR + NUM],
    'STDEV': [ARR + NUM], 'STDEV.S': [ARR + NUM], 'STDEV.P': [ARR + NUM], 'STDEVP': [ARR + NUM], 'STDEVA': [ARR + NUM], 'STDEVPA': [ARR + NUM],
    'DEVSQ': [ARR + NUM], 'AVEDEV': [ARR + NUM], 'GEOMEAN': [ARR + NUM], 'HARMEAN': [ARR + NUM], 'TRIMMEAN': [ARR, P],
    'SKEW': [ARR + NUM], 'SKEW.P': [ARR + NUM], 'KURT': [ARR + NUM], 'STANDARDIZE': [X, X, SD],
    'FREQUENCY': [ARR, ['{2,4}', '{3}', '{1,3,5}', '{5,1}', '"x"']],
    'PROB': [['{0,1,2,3}', '{1,2,3}'], ['{0.2,0.3,0.1,0.4}', '{0.5,0.5}', '{0.2,0.3,0.1,0.5}'], ['0', '1', '2', '1.5'], ['1', '2', '3', '']],
    'CORREL': [ARR + ARR2, ARR2], 'PEARSON': [ARR2, ARR2 + ARR], 'COVAR': [ARR2, ARR2 + ARR], 'COVARIANCE.P': [ARR2, ARR2 + ARR], 'COVARIANCE.S': [ARR2, ARR2 + ARR],
    'RSQ': [ARR2, ARR2 + ARR], 'SLOPE': [ARR2, ARR2 + ARR], 'INTERCEPT': [ARR2, ARR2 + ARR], 'STEYX': [ARR2, ARR2 + ARR],
    'FORECAST': [NUM, ARR2, ARR2 + ARR], 'FORECAST.LINEAR': [NUM, ARR2, ARR2 + ARR],
    'FORECAST.ETS': [['6', '2'], ['{1,2,3,4,5}'], ['{1,2,3,4,5}']],
    'LINEST': [['{1;9;5;7}', '{1,9,5,7}'], ['{0;4;2;3}', '{0,4,2,3}', '{1,2;3,4;5,6;7,9}'], BOOL, BOOL],
    'LOGEST': [['{1;2;4;8}', '{1;0;4;8}'], ['{0;1;2;3}', '{1,2,3,4}'], BOOL, BOOL],
    'TREND': [['{1;9;5;7}'], ['{0;4;2;3}'], ['{5}', '{5;6}', '{1,2}'], BOOL],
    'GROWTH': [['{1;2;4;8}'], ['{0;1;2;3}'], ['{4}', '{4;5}'], BOOL],
    'FISHER': [['0', '0.75', '-0.75', '1', '-1', '0.999999', '"a"', '2']], 'FISHERINV': [['0', '1', '-1', '0.972955', '20', '800', '"a"']],
    'GAMMALN': [['0', '0.5', '1', '2', '4', '100', '171', '1E+10', '-1']], 'GAMMALN.PRECISE': [['0', '0.5', '1', '4', '-1']],
    'GAMMA': [['0', '0.5', '1', '2.5', '5', '170', '171.7', '-1', '-3.75', '-0.5']],
    'PHI': [X], 'GAUSS': [X],
    'CONFIDENCE': [['0.05', '0', '1', '0.5'], SD, ['1', '2', '50', '0', '2.9']], 'CONFIDENCE.NORM': [['0.05', '0', '1', '0.5'], SD, ['1', '2', '50', '0', '2.9']],
    'CONFIDENCE.T': [['0.05', '0', '1', '0.5'], SD, ['1', '2', '50', '0', '2.9']],
    'Z.TEST': [ARR, X, ['', '1', '0', '-1', '10']], 'ZTEST': [ARR, X],
    'T.TEST': [ARR2, ARR2 + ARR, ['1', '2', '3', '0'], ['1', '2', '3', '4']], 'TTEST': [ARR2, ARR2 + ARR, ['1', '2'], ['1', '2', '3']],
    'F.TEST': [ARR2, ARR2 + ARR], 'FTEST': [ARR2, ARR2 + ARR],
    'CHISQ.TEST': [['{58,35;11,25;10,23}', '{1,2}', '{1;2;3}'], ['{45.35,47.65;17.56,18.44;16.09,16.91}', '{1,2}', '{1;2;4}', '{0,1}']],
    'CHITEST': [['{58,35;11,25;10,23}'], ['{45.35,47.65;17.56,18.44;16.09,16.91}']],
    'NORM.DIST': [X, X, SD, BOOL], 'NORMDIST': [X, X, SD, BOOL], 'NORM.S.DIST': [X, BOOL], 'NORMSDIST': [X],
    'NORM.INV': [P, X, SD], 'NORMINV': [P, X, SD], 'NORM.S.INV': [P], 'NORMSINV': [P],
    'LOGNORM.DIST': [X, X, SD, BOOL], 'LOGNORMDIST': [X, X, SD], 'LOGNORM.INV': [P, X, SD], 'LOGINV': [P, X, SD],
    'T.DIST': [X + ['8', '60'], DF, BOOL], 'T.DIST.2T': [X + ['1.96'], DF], 'T.DIST.RT': [X + ['1.96'], DF], 'TDIST': [X, DF, ['1', '2', '3', '0']],
    'T.INV': [P, DF], 'T.INV.2T': [P, DF], 'TINV': [P, DF],
    'CHISQ.DIST': [X + ['18.3'], DF, BOOL], 'CHISQ.DIST.RT': [X + ['18.3'], DF], 'CHIDIST': [X, DF], 'CHISQ.INV': [P, DF], 'CHISQ.INV.RT': [P, DF], 'CHIINV': [P, DF],
    'F.DIST': [X + ['15.2'], DF, DF, BOOL], 'F.DIST.RT': [X + ['15.2'], DF, DF], 'FDIST': [X, DF, DF], 'F.INV': [P, DF, DF], 'F.INV.RT': [P, DF, DF], 'FINV': [P, DF, DF],
    'BETA.DIST': [['0', '0.5', '1', '2', '-1', '1.5'], ['0', '0.5', '2', '8'], ['0.5', '1', '10', '-1'], BOOL, ['0', '1'], ['1', '3']],
    'BETADIST': [['0', '0.5', '1', '2', '-1', '1.5'], ['0', '0.5', '2', '8'], ['0.5', '1', '10', '-1']],
    'BETA.INV': [P, ['0.5', '2', '8', '0'], ['0.5', '1', '10', '-1']], 'BETAINV': [P, ['0.5', '2', '8', '0'], ['0.5', '1', '10', '-1']],
    'GAMMA.DIST': [X + ['10'], ['0.5', '1', '9', '0', '-1'], SD, BOOL], 'GAMMADIST': [X, ['0.5', '1', '9'], SD, BOOL],
    'GAMMA.INV': [P, ['0.5', '1', '9', '0'], SD], 'GAMMAINV': [P, ['0.5', '1', '9'], SD],
    'BINOM.DIST': [COUNT, COUNT, PROB, BOOL], 'BINOMDIST': [COUNT, COUNT, PROB, BOOL],
    'BINOM.DIST.RANGE': [COUNT, PROB, COUNT, ['', '3', '10', '5']], 'BINOM.INV': [COUNT, PROB, PROB], 'CRITBINOM': [COUNT, PROB, PROB],
    'POISSON.DIST': [COUNT + ['1000'], ['0', '0.5', '5', '1000', '-1'], BOOL], 'POISSON': [COUNT, ['0', '5'], BOOL],
    'EXPON.DIST': [X, ['0', '0.5', '10', '-1'], BOOL], 'EXPONDIST': [X, ['0.5', '10'], BOOL],
    'WEIBULL.DIST': [X + ['105'], ['0', '1', '20', '-1'], ['1', '100', '0'], BOOL], 'WEIBULL': [X, ['1', '20'], ['1', '100'], BOOL],
    'HYPGEOM.DIST': [COUNT, COUNT, COUNT, COUNT + ['20'], BOOL], 'HYPGEOMDIST': [COUNT, COUNT, COUNT, COUNT + ['20']],
    'NEGBINOM.DIST': [COUNT, COUNT, PROB, BOOL], 'NEGBINOMDIST': [COUNT, COUNT, PROB],
}

DATES = ['0', '1', '59', '60', '61', '36526', '43831', '44561', '2958465', '2958466', '-1', '"2020-02-29"', '"1/1/2020"', '"12:30 PM"', '"abc"', 'TRUE', '""', '43831.75', '#N/A']
SMALL = ['0', '1', '-1', '2', '12', '-13', '1.9', '"2"', '"x"', 'TRUE']
HOURS = ['0', '12', '23', '24', '27', '-1', '32767', '32768', '1.9', '"x"']
YEARS = ['0', '99', '1899', '1900', '1904', '2020', '9999', '10000', '-1', '2020.9', '"2020"']
WEEKEND = ['', '1', '2', '7', '11', '17', '0', '18', '"0000011"', '"1111111"', '"0100000"', '"01"', '"abcdefg"', 'TRUE']
HOLIDAYS = ['{43831}', '{43831,43832}', '{"x"}', '{43831.9}', '{#N/A}', '{0}']
DATE = {
    'TODAY': [], 'NOW': [],
    'DATE': [YEARS, SMALL + ['13', '24'], SMALL + ['29', '31', '32']],
    'TIME': [HOURS, HOURS, HOURS], 'DATEVALUE': [DATES + ['"2020-1-5"', '"Jan 5, 2020"', '"5-Jan-20"', '"1/5"', '"2020-13-01"', '"29 Feb 1900"', '"1904-01-01"', '"1/1/1899"', '"Mon 1/1/2020"']],
    'TIMEVALUE': [DATES + ['"6:35"', '"6:35:12.5"', '"25:00"', '"1 PM"', '"13:00 PM"', '"1/1/2020 6:00 AM"']],
    'YEAR': [DATES], 'MONTH': [DATES], 'DAY': [DATES], 'HOUR': [DATES + ['0.9999999', '0.5000001']], 'MINUTE': [DATES + ['0.9999999']], 'SECOND': [DATES + ['0.9999999', '0.00001']],
    'WEEKDAY': [DATES, ['', '1', '2', '3', '11', '12', '17', '0', '4', '10', '18', '"x"']],
    'WEEKNUM': [DATES + ['44197', '44198', '44200'], ['', '1', '2', '11', '12', '16', '17', '21', '0', '3', '22']],
    'ISOWEEKNUM': [DATES + ['44197', '43830', '44200']],
    'EDATE': [DATES + ['43861', '43890'], SMALL + ['12', '1200', '-100000']], 'EOMONTH': [DATES + ['43861', '43890'], SMALL + ['12', '1200', '-100000']],
    'DAYS': [DATES, DATES], 'DAYS360': [DATES, DATES, ['', 'TRUE', 'FALSE', '0', '1']],
    'DATEDIF': [DATES, DATES, ['"Y"', '"M"', '"D"', '"MD"', '"YM"', '"YD"', '"y"', '"Q"', '1']],
    'YEARFRAC': [DATES + ['43861', '43890', '43891'], DATES + ['44196', '44561'], ['', '0', '1', '2', '3', '4', '5', '-1', '1.9']],
    'NETWORKDAYS': [DATES + ['44197'], DATES + ['44200', '44561'], HOLIDAYS + ['']],
    'NETWORKDAYS.INTL': [DATES + ['44197'], DATES + ['44200', '44561'], WEEKEND, HOLIDAYS + ['']],
    'WORKDAY': [DATES + ['44197'], SMALL + ['5', '100', '-100', '4000001'], HOLIDAYS + ['']],
    'WORKDAY.INTL': [DATES + ['44197'], SMALL + ['5', '100', '-100'], WEEKEND, HOLIDAYS + ['']],
}


def cases(spec):
    out = []
    for name, domains in spec.items():
        if not domains:
            out.append('%s()' % name)
            continue
        combos = list(itertools.product(*domains))
        stride = max(1, -(-len(combos) // CAP))
        for combo in combos[::stride]:
            args = list(combo)
            # An empty domain value means "argument omitted"; only trailing omissions are written out.
            while args and args[-1] == '':
                args.pop()
            out.append('%s(%s)' % (name, ','.join(args)))
    return out


def write(outdir, family, prefix, spec):
    lines = cases(spec)
    with open(os.path.join(outdir, family + '.fgrid'), 'w', newline='\n') as f, \
            open(os.path.join(outdir, family + '.expected.tsv'), 'w', newline='\n') as e:
        for i, formula in enumerate(lines):
            f.write('%s%d\ta1\t=%s\n' % (prefix, i, formula))
            e.write('%s%d\t=%s\t\n' % (prefix, i, formula))
    return len(lines)


if __name__ == '__main__':
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    print('statistical', write(out, 'statistical', 'st', STAT))
    print('datetime', write(out, 'datetime', 'dt', DATE))
